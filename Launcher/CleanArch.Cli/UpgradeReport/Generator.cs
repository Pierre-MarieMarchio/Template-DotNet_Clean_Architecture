using CleanArch.Tooling.Failures;
using CleanArch.Tooling.Templates;

namespace CleanArch.Cli.UpgradeReport;

internal sealed class Generator(Func<string, TemplateEngine> engineIn, string root, Provenance project, string scratch)
{
    private const string _manualInstructions = "AC1156F7-BB77-4DB8-B28F-24EEBCCA1E5C";

    private const string _withoutSdkTemplates = "--debug:disable-sdk-templates";

    public Generation Generate(Release release)
    {
        string hive = Path.Combine(scratch, "hive-" + release.Version);
        string tree = Path.Combine(scratch, release.Version);
        TemplateEngine engine = engineIn(hive);

        Install(engine, release);
        TemplatePackage content = TemplatePackage.Read(release.File ?? InstalledPackage(hive, release));
        RefuseRunningPostActions(content, release.Version);

        GenerationPlan plan = GenerationPlan.Of(project);
        var notes = new List<string>();
        PackagedTemplate main = content.Require(PackagedTemplate.Main, release.Version);
        Directory.CreateDirectory(tree);
        engine.Run(scratch, [main.ShortName, "-n", project.Name, "-o", tree, .. Arguments(main, plan.Generation, release.Version, notes), _withoutSdkTemplates]);

        foreach (Replay replay in plan.Replays)
        {
            PackagedTemplate item = content.Require(replay.Template, release.Version);
            (string, string)[] values = [.. replay.Values, .. replay.ProjectOptions.Concat(StateOf(tree)).Where(value => item.Declares(value.Item1))];
            engine.Run(tree, [item.ShortName, "-n", replay.Name, .. Arguments(item, values, release.Version, notes), _withoutSdkTemplates]);
        }

        return new Generation(tree, content, notes);
    }

    public static void RefuseRunningPostActions(TemplatePackage content, string version)
    {
        string[] running = [.. content.Templates.Values
            .SelectMany(template => template.Manifest.PostActionIds.Select(id => (template.ShortName, Id: id)))
            .Where(action => !string.Equals(action.Id, _manualInstructions, StringComparison.OrdinalIgnoreCase))
            .Select(action => $"{action.ShortName} {action.Id}")];

        if (running.Length > 0)
        {
            throw new LauncherException(
                $"Version {version} of the package declares post-actions dotnet new would run, beyond printing instructions: {string.Join(", ", running)}. "
                + "The report runs nothing from a package, so it stops here: compare the versions by hand.");
        }
    }

    public static string[] Arguments(PackagedTemplate template, IEnumerable<(string Symbol, string Value)> values, string version, List<string> notes)
    {
        var arguments = new List<string>();
        Dictionary<string, TemplateParameter> parameters = template.ParametersByName();

        foreach ((string symbol, string value) in values)
        {
            if (!parameters.TryGetValue(symbol, out TemplateParameter? parameter))
            {
                notes.Add($"Version {version}'s {template.ShortName} has no option {symbol}, which the project's record sets to {value}: "
                    + "that generation leaves it out, so decide by hand what takes its place.");
                continue;
            }

            if (parameter.Choices.Length > 0 && !parameter.Choices.Contains(value, StringComparer.Ordinal))
            {
                throw new LauncherException(
                    $"Version {version}'s {template.ShortName} does not offer {value} for {template.Option(symbol)} ({string.Join(", ", parameter.Choices)}), "
                    + "the value the project's record holds, so the project cannot be generated from it: decide by hand what replaces it, then compare by hand.");
            }

            arguments.Add(template.Option(symbol));
            arguments.Add(value);
        }

        return [.. arguments];
    }

    private void Install(TemplateEngine engine, Release release)
    {
        try
        {
            engine.Run(root, ["install", release.File ?? $"{project.Package}::{release.Version}", _withoutSdkTemplates]);
        }
        catch (LauncherException error)
        {
            throw new LauncherException(
                $"{error.Message} Installing version {release.Version} of {project.Package} failed: the output above says why.", error);
        }
    }

    private string InstalledPackage(string hive, Release release) =>
        Directory.EnumerateFiles(hive, "*.nupkg", SearchOption.AllDirectories)
            .FirstOrDefault(file => Path.GetFileName(file).StartsWith(project.Package + ".", StringComparison.OrdinalIgnoreCase))
        ?? throw new LauncherException($"dotnet new installed version {release.Version}, and its package is not found under {hive}.");

    private static (string, string)[] StateOf(string tree)
    {
        string presentation = Path.Combine(tree, "Src", "Presentation");
        bool Holds(string pattern) => Directory.Exists(presentation) && Directory.EnumerateDirectories(presentation, pattern).Any();

        return
        [
            ("hasClient", Holds("*.Presentation.Client") ? "true" : "false"),
            ("hasApi", Holds("*.Api") ? "true" : "false"),
            ("workspace", File.Exists(Path.Combine(tree, "Src", "Web", "package.json")) ? "false" : "true"),
        ];
    }
}

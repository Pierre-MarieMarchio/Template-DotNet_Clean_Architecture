using System.Text.RegularExpressions;
using CleanArch.Tooling.Failures;
using CleanArch.Tooling.Manifests;
using CleanArch.Tooling.Projects;
using CleanArch.Tooling.Prompts;

namespace CleanArch.Tooling.Additions;

internal sealed partial class WebPackageAddition(AdditionFlow flow)
{
    public void Add(ProjectFacts project)
    {
        if (!project.HasWorkspace)
        {
            throw new LauncherException($"{project.Name} has no web workspace yet. Add a web app first with web-app: it creates Src/Web.");
        }

        TemplateManifest manifest = flow.Context.Source.Manifest(TemplateKind.WebPackage);
        flow.Conversation.Begin($"Add a package to {project.Name}'s web workspace", flow.Context.Version);
        var detected = new Dictionary<string, string>(StringComparer.Ordinal) { ["scope"] = project.Scope };
        flow.Detected(project, manifest, detected);

        AskedItem asked = flow.AskItem(manifest, "Package name", PackageNameProblem, detected, $"Add it to {project.Name}");
        string name = asked.Name;

        string[] engine = [manifest.ShortName, "-n", name, .. manifest.CommandArguments(asked.Options)];
        ItemStep join = flow.Steps.WebJoin(project.Root, name, []);
        if (flow.Context.DryRun)
        {
            flow.ShowDryRun(engine, [join]);
            return;
        }

        flow.Generate(project, name, engine, [ItemAdditions.WebJoinTask]);
        bool joined = flow.RunStep(project.Root, "Join", "Recording it in cleanarch.json", join);

        flow.Equivalents("web-package", engine, [join.CommandLine]);
        flow.Conversation.Close(joined ? null : new Closing(null, [], $"Record it with {join.CommandLine}.", StepsLeft: true));
    }

    private static string? PackageNameProblem(string name) =>
        PackageName().IsMatch(name) ? null : "use letters, digits and dashes, starting with a letter, like tokens.";

    [GeneratedRegex("^[A-Za-z][A-Za-z0-9-]*$")]
    private static partial Regex PackageName();
}

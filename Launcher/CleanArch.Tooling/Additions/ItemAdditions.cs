using CleanArch.Tooling.Failures;
using CleanArch.Tooling.Manifests;
using CleanArch.Tooling.Projects;
using CleanArch.Tooling.Prompts;
using CleanArch.Tooling.Templates;

namespace CleanArch.Tooling.Additions;

public sealed class ItemAdditions
{
    public const string JoiningSection = "## Joining a project generated without one";
    public const string PresentationCreateSection = "## Create the project";
    public const string Stopped = "Stopped. Nothing was written.";

    public static readonly IReadOnlyDictionary<string, string> PresentationNames = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["blazor-server"] = "Portal",
        ["mcp"] = "Mcp",
        ["avalonia"] = "Desktop",
        ["wpf"] = "Desktop",
        ["worker"] = "Worker",
    };

    private const string _reviewKey = "--review";
    private const string _itemNameSymbol = "name";

    private readonly AdditionFlow _flow;

    public ItemAdditions(AdditionContext context)
    {
        _flow = new AdditionFlow(context);
    }

    public static string? NameRefusal(TemplateManifest manifest, IReadOnlyDictionary<string, string> answers, string name)
    {
        var named = new Dictionary<string, string>(answers, StringComparer.Ordinal) { [_itemNameSymbol] = name };
        return manifest.Refusals.FirstOrDefault(refusal => refusal.When.Symbols.Contains(_itemNameSymbol) && manifest.Holds(refusal.When, named))?.Explanation;
    }

    public static Question Reviewed(Conversation conversation, Func<IReadOnlyDictionary<string, string>, IEnumerable<KeyValuePair<string, string>>> recap, Func<IReadOnlyDictionary<string, string>, string> subject) =>
        new(_reviewKey, (answers, _) =>
        {
            if (conversation.Review(subject(answers), [.. recap(answers)]))
            {
                return Reply.Passed(null);
            }

            return conversation.CanGoBack
                ? Reply.Backward
                : throw new LauncherException(Stopped);
        });

    public static void Install(Conversation conversation, TemplateEngine engine, TemplateSource source, string? templateVersion, string workingDirectory)
    {
        string version = TemplateSource.Installable(templateVersion);
        string package = $"{TemplateSource.PackageId} {version}";
        conversation.Perform($"Installing {package}", $"{package} ready", () => engine.EnsureInstalled(source, version, workingDirectory));
    }

    public static void Equivalents(
        Conversation conversation,
        bool asks,
        string invocation,
        string command,
        IReadOnlyList<string> dotnetNew,
        string? templateSource,
        string? hive,
        bool setup = false,
        IReadOnlyList<string>? then = null)
    {
        List<string> launcher = [command, .. dotnetNew.Skip(1), "--yes"];
        if (setup)
        {
            launcher.Add("--setup");
        }

        if (templateSource is not null)
        {
            launcher.AddRange(["--template-source", templateSource]);
        }

        List<string> engine = [.. dotnetNew];
        if (hive is not null)
        {
            launcher.AddRange(["--hive", hive]);
            engine.AddRange(["--debug:custom-hive", hive]);
        }

        string engineLine = TemplateEngine.CommandLine("dotnet new", engine);
        conversation.Equivalents(
            asks ? [TemplateEngine.CommandLine(invocation, launcher), engineLine, .. then ?? []] : [engineLine, .. then ?? []],
            asks);
    }

    public void WebApp(ProjectFacts project) => new WebAppAddition(_flow).Add(project);

    public void WebPackage(ProjectFacts project) => new WebPackageAddition(_flow).Add(project);

    public void Presentation(ProjectFacts project) => new PresentationAddition(_flow).Add(project);
}

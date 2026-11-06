using System.Text.RegularExpressions;
using CleanArch.Tooling.Failures;
using CleanArch.Tooling.Manifests;
using CleanArch.Tooling.Markdown;
using CleanArch.Tooling.Projects;
using CleanArch.Tooling.Prompts;
using CleanArch.Tooling.Templates;

namespace CleanArch.Tooling.Additions;

internal sealed partial class WebAppAddition(AdditionFlow flow)
{
    private static readonly (string Name, string Symbol)[] _webJoinOptions = [("--web", "framework"), ("--web-audience", "audience"), ("--web-rendering", "rendering"), ("--web-port", "port")];

    public void Add(ProjectFacts project)
    {
        TemplateManifest manifest = flow.Context.Source.Manifest(TemplateKind.WebApp);
        flow.Conversation.Begin($"Add a web app to {project.Name}", flow.Context.Version);
        var detected = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["workspace"] = AdditionFlow.Flag(!project.HasWorkspace),
            ["scope"] = project.Scope,
            ["projectName"] = project.Name,
            ["auth"] = project.Auth,
            ["examples"] = project.Examples,
            ["files"] = AdditionFlow.Flag(project.Files),
            ["hasClient"] = AdditionFlow.Flag(project.HasClient),
            ["ci"] = AdditionFlow.Flag(project.Ci),
            ["agentFiles"] = AdditionFlow.Flag(project.AgentFiles),
        };
        flow.Detected(project, manifest, detected);

        AskedItem asked = flow.AskItem(
            manifest,
            "App name",
            name => AppNameProblem(name) ?? (Directory.Exists(AppFolder(project, name)) ? "there's already an app with that name." : null),
            detected,
            $"Add it to {project.Name}");
        string name = asked.Name;
        Refuse(project, manifest, asked);

        string[] engine = [manifest.ShortName, "-n", name, .. manifest.CommandArguments(asked.Options)];
        string[] join = AdditionFlow.TaskRun([ItemAdditions.WebJoinTask, name, .. JoinOptions(manifest, asked.Options)]);
        if (flow.Context.DryRun)
        {
            flow.ShowDryRun(engine, [join]);
            return;
        }

        flow.Generate(project, name, engine, [ItemAdditions.WebJoinTask]);
        bool joined = flow.RunTask(project.Root, "Join", "Making the edits its README lists outside its folder and recording it in cleanarch.json", join);

        flow.Equivalents("web-app", engine, [TemplateEngine.CommandLine(AdditionFlow.Dotnet, join)]);
        Close(project, manifest, asked, joined);
    }

    private void Close(ProjectFacts project, TemplateManifest manifest, AskedItem asked, bool joined)
    {
        bool workspace = Condition.IsTruthy(manifest.Resolve("workspace", asked.Options));
        string readme = workspace && !joined ? Path.Combine(project.WebFolder, "README.md") : Path.Combine(AppFolder(project, asked.Name), "README.md");
        string heading = joined ? "## Join the stack" : ItemAdditions.JoiningSection;
        flow.Conversation.Close(new Closing(Path.GetRelativePath(project.Root, readme).Replace(Path.DirectorySeparatorChar, '/'), [], MarkdownSection.Of(readme, heading) ?? $"Read {readme}.", StepsLeft: !joined));
    }

    private static void Refuse(ProjectFacts project, TemplateManifest manifest, AskedItem asked)
    {
        string folder = AppFolder(project, asked.Name);
        if (Directory.Exists(folder))
        {
            throw new LauncherException($"There's already an app in '{folder}'. Pick another name with --name.");
        }

        if (manifest.Resolve("framework", asked.Options) == "blazor-wasm" && project.BlazorWebAssemblyApp is { } existing)
        {
            throw new LauncherException($"{project.Name} already has a Blazor WebAssembly app, Src/Web/apps/{existing}. This version adds only one per project.");
        }
    }

    private static IEnumerable<string> JoinOptions(TemplateManifest manifest, IReadOnlyDictionary<string, string> answers) =>
        _webJoinOptions.SelectMany(option => manifest.Resolve(option.Symbol, answers) is { } value ? [option.Name, value] : Array.Empty<string>());

    private static string AppFolder(ProjectFacts project, string name) => Path.Combine(project.WebFolder, "apps", name);

    private static string? AppNameProblem(string name) =>
        AppName().IsMatch(name) ? null : "use PascalCase, like BackOffice. It becomes the app's folder, service and port variable.";

    [GeneratedRegex("^[A-Z][A-Za-z0-9]*$")]
    private static partial Regex AppName();
}

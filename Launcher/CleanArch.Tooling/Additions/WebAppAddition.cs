using System.Text.RegularExpressions;
using CleanArch.Tooling.Failures;
using CleanArch.Tooling.Manifests;
using CleanArch.Tooling.Markdown;
using CleanArch.Tooling.Projects;
using CleanArch.Tooling.Prompts;

namespace CleanArch.Tooling.Additions;

internal sealed partial class WebAppAddition(AdditionFlow flow)
{
    private const string _command = "web-app";
    private const string _createSection = "## Create the app";

    public void Add(ProjectFacts project)
    {
        TemplateManifest manifest = flow.Context.Source.Manifest(TemplateKind.WebApp);
        if (flow.Existing(name => AppFolder(project, name), AppName().IsMatch) is { } existing)
        {
            Complete(project, manifest, existing);
            return;
        }

        flow.Conversation.Begin($"Add a web app to {project.Name}", flow.Context.Version);
        var detected = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["workspace"] = Condition.Of(!project.HasWorkspace),
            ["scope"] = project.Scope,
            ["projectName"] = project.Name,
            ["auth"] = project.Auth,
            ["examples"] = project.Examples,
            ["files"] = Condition.Of(project.Files),
            ["hasClient"] = Condition.Of(project.HasClient),
            ["ci"] = Condition.Of(project.Ci),
            ["agentFiles"] = Condition.Of(project.AgentFiles),
        };
        flow.Detected(project, manifest, detected);

        AskedItem asked = flow.AskItem(
            manifest,
            "App name",
            name => AppNameProblem(name) ?? (Directory.Exists(AppFolder(project, name)) ? $"there's already an app with that name: {flow.CompletedBy(_command, name)} completes it." : null),
            detected,
            $"Add it to {project.Name}");
        string name = asked.Name;
        Refuse(project, manifest, asked);

        string[] engine = [manifest.ShortName, "-n", name, .. manifest.CommandArguments(asked.Options)];
        PlannedStep[] steps = Steps(project, name, JoinOptions(symbol => manifest.Resolve(symbol, asked.Options)));
        if (flow.Context.DryRun)
        {
            flow.ShowDryRun(engine, steps);
            return;
        }

        flow.Generate(project, name, engine, [ItemAdditions.WebJoinTask, ItemAdditions.WebScaffoldTask]);
        string rerun = flow.CompletedBy(_command, name);
        int done = flow.RunSteps(project.Root, steps, rerun);

        flow.Equivalents(_command, engine, [rerun]);
        Close(project, manifest, asked, done);
    }

    private void Complete(ProjectFacts project, TemplateManifest manifest, string name)
    {
        flow.Conversation.Begin($"Complete Src/Web/apps/{name} in {project.Name}", flow.Context.Version);
        Dictionary<string, string> given = flow.GivenOptions(manifest);
        PlannedStep[] steps = Steps(project, name, JoinOptions(given.GetValueOrDefault));
        if (flow.Complete(project.Root, $"Src/Web/apps/{name}", "joined and created", steps, flow.CompletedBy(_command, name)) is { } done)
        {
            Close(project, manifest, new AskedItem(name, given), done);
        }
    }

    private PlannedStep[] Steps(ProjectFacts project, string name, WebJoinOptions joinOptions) =>
    [
        new("Join", "Making the edits its README lists outside its folder and recording it in cleanarch.json", flow.Steps.WebJoin(project.Root, name, joinOptions)),
        new("Scaffold", "Creating it with its framework's official CLI, which needs the network", flow.Steps.WebScaffold(project.Root, name)),
    ];

    private void Close(ProjectFacts project, TemplateManifest manifest, AskedItem asked, int done)
    {
        bool workspace = Condition.IsTruthy(manifest.Resolve("workspace", asked.Options));
        string readme = workspace && done == 0 ? Path.Combine(project.WebFolder, "README.md") : Path.Combine(AppFolder(project, asked.Name), "README.md");
        string heading = done switch
        {
            0 => ItemAdditions.JoiningSection,
            1 => _createSection,
            _ => "## Join the stack",
        };
        flow.Conversation.Close(new Closing(Path.GetRelativePath(project.Root, readme).Replace(Path.DirectorySeparatorChar, '/'), [], MarkdownSection.Of(readme, heading) ?? $"Read {readme}.", StepsLeft: done < 2));
    }

    private static void Refuse(ProjectFacts project, TemplateManifest manifest, AskedItem asked)
    {
        if (manifest.Resolve("framework", asked.Options) == "blazor-wasm" && project.BlazorWebAssemblyApp is { } existing)
        {
            throw new LauncherException($"{project.Name} already has a Blazor WebAssembly app, Src/Web/apps/{existing}. This version adds only one per project.");
        }
    }

    private static WebJoinOptions JoinOptions(Func<string, string?> valueOf) =>
        new(valueOf("framework"), valueOf("audience"), valueOf("rendering"), valueOf("port"));

    private static string AppFolder(ProjectFacts project, string name) => Path.Combine(project.WebFolder, "apps", name);

    private static string? AppNameProblem(string name) =>
        AppName().IsMatch(name) ? null : "use PascalCase, like BackOffice. It becomes the app's folder, service and port variable.";

    [GeneratedRegex("^[A-Z][A-Za-z0-9]*$")]
    private static partial Regex AppName();
}

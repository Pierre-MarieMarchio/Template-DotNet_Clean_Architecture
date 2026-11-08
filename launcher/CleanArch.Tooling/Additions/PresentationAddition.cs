using System.Text.RegularExpressions;
using CleanArch.Tooling.Manifests;
using CleanArch.Tooling.Markdown;
using CleanArch.Tooling.Projects;
using CleanArch.Tooling.Prompts;

namespace CleanArch.Tooling.Additions;

internal sealed partial class PresentationAddition(AdditionFlow flow)
{
    private const string _frameworkSymbol = "framework";
    private const string _command = "presentation";

    public void Add(ProjectFacts project)
    {
        TemplateManifest manifest = flow.Context.Source.Manifest(TemplateKind.Presentation);
        if (flow.Existing(name => Path.Combine(project.PresentationFolder, project.FolderOf(name)), PresentationName().IsMatch) is { } existing)
        {
            Complete(project, manifest, existing);
            return;
        }

        flow.Conversation.Begin($"Add a presentation project to {project.Name}", flow.Context.Version);
        var detected = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["projectName"] = project.Name,
            ["auth"] = project.Auth,
            ["examples"] = project.Examples,
            ["files"] = Condition.Of(project.Files),
            ["hasClient"] = Condition.Of(project.HasClient),
            ["database"] = project.Database,
            ["agentFiles"] = Condition.Of(project.AgentFiles),
            ["hasApi"] = Condition.Of(project.HasApi),
            ["ci"] = Condition.Of(project.Ci),
        };
        flow.Detected(project, manifest, detected);

        AskedItem asked = flow.AskItemNamedLast(
            manifest,
            "Presentation name",
            (answers, name) => PresentationNameProblem(manifest, project, answers, name),
            (answers, remembered) => ProposedPresentationName(manifest, answers, remembered),
            detected,
            $"Add it to {project.Name}");
        string name = asked.Name;
        string presentation = $"{project.Name}.{name}";

        string[] engine = [manifest.ShortName, "-n", name, .. manifest.CommandArguments(asked.Options)];
        PlannedStep[] steps = Steps(project, presentation);
        if (flow.Context.DryRun)
        {
            flow.ShowDryRun(engine, steps);
            return;
        }

        flow.Generate(project, name, engine, [flow.Completion(_command)]);
        string rerun = flow.CompletedBy(_command, name);
        int done = flow.RunSteps(project.Root, steps, rerun);

        flow.Equivalents(_command, engine, [rerun]);
        Close(project, manifest, asked, done);
    }

    private void Complete(ProjectFacts project, TemplateManifest manifest, string name)
    {
        string presentation = $"{project.Name}.{name}";
        flow.Conversation.Begin($"Complete {presentation} in {project.Name}", flow.Context.Version);
        if (flow.Complete(project.Root, $"src/Presentation/{project.FolderOf(name)}", "joined and created", Steps(project, presentation), flow.CompletedBy(_command, name)) is { } done)
        {
            Close(project, manifest, new AskedItem(name, flow.GivenOptions(manifest)), done);
        }
    }

    private PlannedStep[] Steps(ProjectFacts project, string presentation) =>
    [
        new("Join", "Making the edits its README lists outside its folders", flow.Steps.PresentationJoin(project.Root, presentation)),
        new("Scaffold", "Creating it with its framework's dotnet new template, which needs the network", flow.Steps.PresentationScaffold(project.Root, presentation)),
    ];

    private void Close(ProjectFacts project, TemplateManifest manifest, AskedItem asked, int done)
    {
        bool joined = done >= 1;
        bool created = done >= 2;
        string folder = project.FolderOf(asked.Name);
        string readme = Path.Combine(project.PresentationFolder, folder, "README.md");
        string shown = $"src/Presentation/{folder}/README.md";
        string[] identityStep = project.Auth == "local" && manifest.Resolve(_frameworkSymbol, asked.Options) == "mcp"
            ? [$"{flow.Context.Tool} migrations add AddAccessKeys --context auth"]
            : [];
        if (created)
        {
            flow.Conversation.Close(new Closing(
                null,
                identityStep,
                $"Commit src/Presentation/{folder} and its tests: from now on the pages are this project's own. {shown} says how the project was created."));
            return;
        }

        string heading = joined ? ItemAdditions.PresentationCreateSection : ItemAdditions.JoiningSection;
        flow.Conversation.Close(new Closing(shown, identityStep, MarkdownSection.Of(readme, heading) ?? $"Read {shown}.", StepsLeft: true));
    }

    private static string? ProposedPresentationName(TemplateManifest manifest, IReadOnlyDictionary<string, string> answers, string? remembered)
    {
        if (remembered is not null && !ItemAdditions.PresentationNames.Values.Contains(remembered))
        {
            return remembered;
        }

        return answers.GetValueOrDefault(_frameworkSymbol) is { } framework && ItemAdditions.PresentationNames.TryGetValue(framework, out string? name)
            ? name
            : manifest.DefaultName;
    }

    private string? PresentationNameProblem(TemplateManifest manifest, ProjectFacts project, IReadOnlyDictionary<string, string> answers, string name)
    {
        if (!PresentationName().IsMatch(name))
        {
            return $"use PascalCase, like Portal. {project.Name}.<name> becomes the project's name and service, in src/Presentation/{project.FolderOf("<name>")}.";
        }

        if (ItemAdditions.NameRefusal(manifest, answers, name) is { } refused)
        {
            return refused;
        }

        string folder = Path.Combine(project.PresentationFolder, project.FolderOf(name));
        return Directory.Exists(folder) ? $"there's already a presentation project with that name: {flow.CompletedBy(_command, name)} completes it." : null;
    }

    [GeneratedRegex("^[A-Z][A-Za-z0-9]*$")]
    private static partial Regex PresentationName();
}

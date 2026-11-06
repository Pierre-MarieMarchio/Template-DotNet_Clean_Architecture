using System.Text.RegularExpressions;
using CleanArch.Tooling.Manifests;
using CleanArch.Tooling.Markdown;
using CleanArch.Tooling.Projects;
using CleanArch.Tooling.Prompts;
using CleanArch.Tooling.Templates;

namespace CleanArch.Tooling.Additions;

internal sealed partial class PresentationAddition(AdditionFlow flow)
{
    private const string _frameworkSymbol = "framework";

    public void Add(ProjectFacts project)
    {
        TemplateManifest manifest = flow.Context.Source.Manifest(TemplateKind.Presentation);
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
        string[] join = AdditionFlow.TaskRun(ItemAdditions.PresentationJoinTask, presentation);
        string[] scaffold = AdditionFlow.TaskRun(ItemAdditions.PresentationScaffoldTask, presentation);
        if (flow.Context.DryRun)
        {
            flow.ShowDryRun(engine, [join, scaffold]);
            return;
        }

        flow.Generate(project, name, engine, [ItemAdditions.PresentationJoinTask, ItemAdditions.PresentationScaffoldTask]);

        string joinLine = TemplateEngine.CommandLine(AdditionFlow.Dotnet, join);
        string scaffoldLine = TemplateEngine.CommandLine(AdditionFlow.Dotnet, scaffold);
        bool joined = flow.RunTask(project.Root, "Join", "Making the edits its README lists outside its folders", join);
        bool created = joined && flow.RunTask(project.Root, "Scaffold", "Creating it with its framework's dotnet new template, which needs the network", scaffold);
        if (!joined)
        {
            flow.Conversation.Reported("Scaffold skipped", false, "Skipped because the join step didn't go through.", [scaffoldLine]);
        }

        flow.Equivalents("presentation", engine, [joinLine, scaffoldLine]);
        Close(project, manifest, asked, joined, created);
    }

    private void Close(ProjectFacts project, TemplateManifest manifest, AskedItem asked, bool joined, bool created)
    {
        string folder = project.FolderOf(asked.Name);
        string readme = Path.Combine(project.PresentationFolder, folder, "README.md");
        string shown = $"Src/Presentation/{folder}/README.md";
        string[] identityStep = project.Auth == "local" && manifest.Resolve(_frameworkSymbol, asked.Options) == "mcp"
            ? [TemplateEngine.CommandLine(AdditionFlow.Dotnet, AdditionFlow.TaskRun("migration-add", "AddAccessKeys", "--context", "auth"))]
            : [];
        if (created)
        {
            flow.Conversation.Close(new Closing(
                null,
                identityStep,
                $"Commit Src/Presentation/{folder} and its tests: from now on the pages are this project's own. {shown} says how the project was created."));
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

    private static string? PresentationNameProblem(TemplateManifest manifest, ProjectFacts project, IReadOnlyDictionary<string, string> answers, string name)
    {
        if (!PresentationName().IsMatch(name))
        {
            return $"use PascalCase, like Portal. {project.Name}.<name> becomes the project's name and service, in Src/Presentation/{project.FolderOf("<name>")}.";
        }

        if (ItemAdditions.NameRefusal(manifest, answers, name) is { } refused)
        {
            return refused;
        }

        string folder = Path.Combine(project.PresentationFolder, project.FolderOf(name));
        return Directory.Exists(folder) ? "there's already a presentation project with that name." : null;
    }

    [GeneratedRegex("^[A-Z][A-Za-z0-9]*$")]
    private static partial Regex PresentationName();
}

using System.Collections.ObjectModel;
using CleanArch.Tooling;
using CleanArch.Tooling.Additions;
using CleanArch.Tooling.Processes;
using CleanArch.Tooling.Templates;

namespace CleanArch.Cli;

internal sealed class ProjectItemSteps(IProcessRunner processes) : IItemSteps
{
    private const string _dotnet = "dotnet";

    public ItemStep WebJoin(string root, string name, IReadOnlyList<string> options) =>
        Task(root, [ItemAdditions.WebJoinTask, name, .. options], () => Recorded(root)?.Web is { } web && (web.Apps.Any(app => app.Name == name) || web.Packages.Contains(name)));

    public ItemStep WebScaffold(string root, string name) =>
        Task(root, [ItemAdditions.WebScaffoldTask, name], () => !ScaffoldDescriptor.In(Path.Combine(root, ProjectLayout.Web, "apps", name)));

    public ItemStep PresentationJoin(string root, string project) =>
        Task(root, [ItemAdditions.PresentationJoinTask, project], () => Recorded(root) is { } file && file.Presentation?.Any(presentation => presentation.Name == PartOf(file, project)) == true);

    public ItemStep PresentationScaffold(string root, string project) =>
        Task(root, [ItemAdditions.PresentationScaffoldTask, project], () => Recorded(root) is { } file && !ScaffoldDescriptor.In(Path.Combine(root, "Src", "Presentation", ProjectNames.FolderOf(file.Name, PartOf(file, project)))));

    private static ProjectFile? Recorded(string root)
    {
        try
        {
            return ProjectLocator.Find(root)?.File;
        }
        catch (ProjectFileException)
        {
            return null;
        }
    }

    private static string PartOf(ProjectFile file, string project) =>
        project.StartsWith(file.Name + ".", StringComparison.Ordinal) ? project[(file.Name.Length + 1)..] : project;

    private ItemStep Task(string root, IReadOnlyList<string> task, Func<bool> isDone)
    {
        string[] arguments = ["run", "Tools/Tasks.cs", .. task];
        return new ItemStep(
            TemplateEngine.CommandLine(_dotnet, arguments),
            isDone,
            line => processes.Run(new ProcessCall(_dotnet, root, arguments, ReadOnlyDictionary<string, string>.Empty), line));
    }
}

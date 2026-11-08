using System.Text;
using CleanArch.Tooling;
using CleanArch.Tooling.Additions;
using CleanArch.Tooling.Processes;

namespace CleanArch.Cli;

internal sealed class ProjectItemSteps(IProcessLauncher processes) : IItemSteps
{
    public ItemStep WebJoin(string root, string name, WebJoinOptions options) =>
        new(
            () => Recorded(root)?.Web is { } web && (web.Apps.Any(app => app.Name == name) || web.Packages.Contains(name)),
            line => InProcess(root, line, (project, shell) => Joins.WebJoin.Run(project, name, options, shell.Output, shell.Error)));

    public ItemStep WebScaffold(string root, string name) =>
        new(
            () => !ScaffoldDescriptor.In(Path.Combine(root, ProjectLayout.Web, "apps", name)),
            line => InProcess(root, line, (project, shell) => Scaffolds.WebApps(project, shell, name)));

    public ItemStep PresentationJoin(string root, string project) =>
        new(
            () => Recorded(root) is { } file && file.Presentation?.Any(presentation => presentation.Name == PartOf(file, project)) == true,
            line => InProcess(root, line, (located, shell) => Joins.PresentationJoin.Run(located, project, processes, shell.Output, shell.Error)));

    public ItemStep PresentationScaffold(string root, string project) =>
        new(
            () => Recorded(root) is { } file && !ScaffoldDescriptor.In(Path.Combine(root, "src", "Presentation", ProjectNames.FolderOf(file.Name, PartOf(file, project)))),
            line => InProcess(root, line, (located, shell) => Scaffolds.Presentations(located, shell, project)));

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

    private ProcessResult InProcess(string root, Action<string>? line, Func<LocatedProject, Shell, int> step)
    {
        var captured = new StringBuilder();
        using var output = new LineWriter(line, captured);
        var shell = new Shell(processes, output, output);
        int exit = TestRuns.Guarded(shell, () => step(Located(root), shell));
        output.Flush();
        return new ProcessResult(exit, captured.ToString());
    }

    private static LocatedProject Located(string root)
    {
        try
        {
            return ProjectLocator.Find(root) ?? throw new InvalidOperationException($"{root} holds no {ProjectLocator.FileName}, so the addition cannot be completed there.");
        }
        catch (ProjectFileException unreadable)
        {
            throw new InvalidOperationException(unreadable.Message, unreadable);
        }
    }
}

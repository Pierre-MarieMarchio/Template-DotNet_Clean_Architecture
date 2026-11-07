using System.Collections.ObjectModel;
using CleanArch.Tooling.Additions;
using CleanArch.Tooling.Processes;
using CleanArch.Tooling.Templates;

namespace CleanArch.Cli;

internal sealed class ProjectItemSteps(IProcessRunner processes) : IItemSteps
{
    private const string _dotnet = "dotnet";

    public ItemStep WebJoin(string root, string name, IReadOnlyList<string> options) => Task(root, [ItemAdditions.WebJoinTask, name, .. options]);

    public ItemStep PresentationJoin(string root, string project) => Task(root, [ItemAdditions.PresentationJoinTask, project]);

    public ItemStep PresentationScaffold(string root, string project) => Task(root, [ItemAdditions.PresentationScaffoldTask, project]);

    private ItemStep Task(string root, IReadOnlyList<string> task)
    {
        string[] arguments = ["run", "Tools/Tasks.cs", .. task];
        return new ItemStep(
            TemplateEngine.CommandLine(_dotnet, arguments),
            line => processes.Run(new ProcessCall(_dotnet, root, arguments, ReadOnlyDictionary<string, string>.Empty), line));
    }
}

using CleanArch.Cli.Commands.Kernel;

namespace CleanArch.Cli.Commands.Check;

internal sealed record CheckPlan(
    string Root,
    string Solution,
    int Touched,
    CheckScope Scope,
    string[] Formatted,
    string[] Built,
    string[] Tested,
    string[] Skipped,
    string[] NotRun)
{
    public string ScopeLine => Scope.BuildWide
        ? "A file that sets the build for every project is touched: the whole solution is built and its Docker-free tests run."
        : $"Touched: {Touched} files, {Scope.Projects.Count} projects with the ones that reference them.";

    public string Summary =>
        $"Format of {Formatted.Length} C# file(s), build of {(Scope.BuildWide ? "the solution" : Built.Length + " project(s)")}, "
        + $"tests of {(Scope.BuildWide ? "every Docker-free project" : Tested.Length + " project(s)")}: all passed; "
        + $"{NotRun.Length} project(s) that need Docker were built and not run. "
        + "The integration tests, coverage and the other gates of verify are left to verify, coverage and CI.";

    public static CheckPlan Of(LocatedProject project, string[] changed, bool onWindows)
    {
        string root = project.Root;
        string solution = Path.Combine(root, ProjectLayout.Solution(project));
        SolutionProject[] graph = ProjectGraph.Read(root, solution);
        string architecture = ProjectPath.Shown(ProjectLayout.ArchitectureTests(project));
        if (graph.All(node => node.Path != architecture))
        {
            throw new InvalidOperationException($"The solution holds no {architecture}, the project whose rules every check runs.");
        }

        CheckScope scope = ProjectGraph.ScopeOf(changed, graph);
        string[] formatted = ProjectGraph.FormattableFiles(changed, graph, file => File.Exists(Path.Combine(root, file)));
        string[] built =
        [
            .. scope.Projects.Concat(graph.Where(node => node.NeedsDocker).Select(node => node.Path)).Append(architecture).Distinct(StringComparer.Ordinal),
        ];
        (string[] tested, string[] skipped, string[] notRun) = TestedProjects(built, graph, architecture, onWindows);
        return new CheckPlan(root, solution, changed.Length, scope, formatted, built, tested, skipped, notRun);
    }

    public static (string[] Tested, string[] Skipped, string[] NotRun) TestedProjects(IEnumerable<string> built, IReadOnlyList<SolutionProject> graph, string architecture, bool onWindows)
    {
        SolutionProject Node(string path) => graph.First(node => node.Path == path);
        string[] candidates = [.. built.Where(path => path == architecture || Node(path).DockerFreeTests)];
        string[] skipped = onWindows ? [] : [.. candidates.Where(path => Node(path).WindowsOnly)];
        return ([.. candidates.Except(skipped, StringComparer.Ordinal)], skipped, [.. built.Where(path => Node(path).NeedsDocker)]);
    }
}

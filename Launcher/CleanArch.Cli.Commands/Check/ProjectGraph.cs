using System.Xml.Linq;
using CleanArch.Cli.Commands.Kernel;

namespace CleanArch.Cli.Commands.Check;

internal static class ProjectGraph
{
    private static readonly string[] _buildWideFileNames = ["Directory.Build.rsp", "global.json", "nuget.config", ".editorconfig", ".globalconfig"];

    private static readonly string[] _buildWideExtensions = [".csproj", ".props", ".targets", ".sln", ".slnx"];

    public static SolutionProject[] Read(string root, string solution) =>
        [.. SolutionFilter.ProjectPaths(solution).Select(path => NodeOf(root, path))];

    public static CheckScope ScopeOf(IEnumerable<string> changed, IReadOnlyList<SolutionProject> graph)
    {
        string[] files = [.. changed];
        if (files.Any(file => SetsTheBuildForEveryProject(file, graph)))
        {
            return new CheckScope(BuildWide: true, [.. graph.Select(project => project.Path)]);
        }

        HashSet<string> affected = [.. files.Select(file => OwnerOf(file, graph)).OfType<SolutionProject>().Select(project => project.Path)];
        for (bool grew = true; grew;)
        {
            SolutionProject[] dependents = [.. graph.Where(project => !affected.Contains(project.Path) && project.References.Any(affected.Contains))];
            grew = dependents.Length > 0;
            affected.UnionWith(dependents.Select(project => project.Path));
        }

        return new CheckScope(BuildWide: false, [.. affected.Order(StringComparer.Ordinal)]);
    }

    public static string[] FormattableFiles(IEnumerable<string> changed, IReadOnlyList<SolutionProject> graph, Func<string, bool> exists) =>
        [.. changed.Where(file => file.EndsWith(".cs", StringComparison.Ordinal) && exists(file) && OwnerOf(file, graph) is not null).Order(StringComparer.Ordinal)];

    private static SolutionProject NodeOf(string root, string path)
    {
        string full = Path.GetFullPath(Path.Combine(root, path));
        string directory = Path.GetDirectoryName(full)!;
        string[] references =
        [
            .. XDocument.Parse(File.ReadAllText(full)).Descendants("ProjectReference")
                .Select(reference => reference.Attribute("Include")?.Value)
                .OfType<string>()
                .Select(include => ProjectPath.Relative(root, Path.GetFullPath(Path.Combine(directory, include.Replace('\\', '/'))))),
        ];
        return new SolutionProject(path, ProjectPath.Shown(Path.GetDirectoryName(path)!), references, TestProjects.IsDockerFree(full), TestProjects.WindowsFramework(full) is not null);
    }

    private static SolutionProject? OwnerOf(string file, IReadOnlyList<SolutionProject> graph) =>
        graph.Where(project => file.StartsWith(project.Folder + "/", StringComparison.Ordinal)).MaxBy(project => project.Folder.Length);

    private static bool SetsTheBuildForEveryProject(string file, IReadOnlyList<SolutionProject> graph)
    {
        int slash = file.LastIndexOf('/');
        string name = file[(slash + 1)..];
        string folder = slash < 0 ? string.Empty : file[..slash];
        bool buildWide = _buildWideFileNames.Contains(name, StringComparer.OrdinalIgnoreCase)
            || _buildWideExtensions.Any(extension => name.EndsWith(extension, StringComparison.OrdinalIgnoreCase));
        return buildWide && (folder.Length == 0 || graph.Any(project => Nests(project.Folder, folder)));
    }

    private static bool Nests(string projectFolder, string folder) =>
        projectFolder == folder
        || projectFolder.StartsWith(folder + "/", StringComparison.Ordinal)
        || folder.StartsWith(projectFolder + "/", StringComparison.Ordinal);
}

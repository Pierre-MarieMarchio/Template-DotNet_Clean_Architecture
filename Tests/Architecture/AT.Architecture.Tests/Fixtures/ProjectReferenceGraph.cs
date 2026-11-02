using System.Xml.Linq;

namespace AppTemplate.Architecture.Tests.Fixtures;

internal sealed record ProjectNode(
    string Name,
    string RelativePath,
    IReadOnlySet<string> References,
    bool IsSdkProject,
    bool HasProgram,
    string Sdk,
    IReadOnlySet<string> FrameworkReferences,
    IReadOnlySet<string> Packages);

internal static class ProjectReferenceGraph
{
    private const string _infrastructurePrefix = "AppTemplate.Infrastructure.";

    private const string _clientProject = "AppTemplate.Client";

    internal static string RepositoryRoot { get; } = LocateRepositoryRoot();

    internal static IReadOnlyDictionary<string, ProjectNode> SourceProjects { get; } = ReadSourceProjects();

    internal static bool IsInfrastructureModule(string projectName) =>
        projectName.StartsWith(_infrastructurePrefix, StringComparison.Ordinal)
        && !IsSdkProject(projectName);

    internal static IEnumerable<ProjectNode> InfrastructureModules =>
        SourceProjects.Values.Where(project => IsInfrastructureModule(project.Name));

    internal static bool IsClient(ProjectNode project) =>
        project.Name != _clientProject && ClosureOf(project).Any(reached => reached.Name == _clientProject);

    internal static IEnumerable<ProjectNode> Clients => SourceProjects.Values.Where(IsClient);

    internal static bool IsHost(ProjectNode project) => project is { HasProgram: true } && !IsClient(project);

    internal static IEnumerable<ProjectNode> Hosts => SourceProjects.Values.Where(IsHost);

    internal static IEnumerable<ProjectNode> SdkProjects =>
        SourceProjects.Values.Where(project => project.IsSdkProject);

    internal static bool IsSdkProject(string projectName) =>
        SourceProjects.TryGetValue(projectName, out var project) && project.IsSdkProject;

    internal static IEnumerable<ProjectNode> ProjectsInLayer(string layerFolder)
    {
        string prefix = Path.Combine("Src", layerFolder) + Path.DirectorySeparatorChar;

        return SourceProjects.Values
            .Where(project => project.RelativePath.StartsWith(prefix, StringComparison.Ordinal))
            .OrderBy(project => project.Name, StringComparer.Ordinal);
    }

    internal static IReadOnlyList<ProjectNode> ClosureOf(ProjectNode project)
    {
        var seen = new Dictionary<string, ProjectNode>(StringComparer.Ordinal) { [project.Name] = project };
        var pending = new Stack<ProjectNode>([project]);

        while (pending.TryPop(out var current))
        {
            foreach (string reference in current.References)
            {
                if (SourceProjects.TryGetValue(reference, out var next) && seen.TryAdd(next.Name, next))
                {
                    pending.Push(next);
                }
            }
        }

        return [.. seen.Values.OrderBy(node => node.Name, StringComparer.Ordinal)];
    }

    internal static string RootOf(ProjectNode project) =>
        Path.GetDirectoryName(Path.Combine(RepositoryRoot, project.RelativePath))!;

    internal static ProjectNode Project(string name) =>
        SourceProjects.TryGetValue(name, out var project)
            ? project
            : throw new InvalidOperationException(
                $"No project named '{name}' was found under '{RepositoryRoot}\\Src'. Known projects: " +
                string.Join(", ", SourceProjects.Keys.Order(StringComparer.Ordinal)));

    private static Dictionary<string, ProjectNode> ReadSourceProjects()
    {
        string sourceRoot = Path.Combine(RepositoryRoot, "Src");
        var projects = new Dictionary<string, ProjectNode>(StringComparer.Ordinal);

        foreach (string projectFile in Directory.EnumerateFiles(sourceRoot, "*.csproj", SearchOption.AllDirectories))
        {
            string name = Path.GetFileNameWithoutExtension(projectFile);
            var document = XDocument.Load(projectFile);

            projects[name] = new ProjectNode(
                name,
                Path.GetRelativePath(RepositoryRoot, projectFile),
                ReadProjectReferences(document),
                ReadIsSdkProject(document),
                File.Exists(Path.Combine(Path.GetDirectoryName(projectFile)!, "Program.cs")),
                document.Root?.Attribute("Sdk")?.Value ?? string.Empty,
                ReadIncludes(document, "FrameworkReference"),
                ReadIncludes(document, "PackageReference"));
        }

        if (projects.Count == 0)
        {
            throw new InvalidOperationException(
                $"No project files were found under '{sourceRoot}'. Every rule written against the " +
                "reference graph would pass for the wrong reason.");
        }

        return projects;
    }

    private static HashSet<string> ReadProjectReferences(XDocument document) =>
        document
            .Descendants("ProjectReference")
            .Select(element => element.Attribute("Include")?.Value)
            .Where(include => !string.IsNullOrWhiteSpace(include))
            .Select(include => Path.GetFileNameWithoutExtension(
                include!.Replace('\\', Path.DirectorySeparatorChar)))
            .ToHashSet(StringComparer.Ordinal);

    private static HashSet<string> ReadIncludes(XDocument document, string itemName) =>
        document
            .Descendants(itemName)
            .Select(element => element.Attribute("Include")?.Value)
            .Where(include => !string.IsNullOrWhiteSpace(include))
            .Select(include => include!)
            .ToHashSet(StringComparer.Ordinal);

    private static bool ReadIsSdkProject(XDocument document) =>
        document
            .Descendants("IsSdkProject")
            .Any(element => bool.TryParse(element.Value.Trim(), out bool marked) && marked);

    private static string LocateRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            bool hasCentralPackages = File.Exists(Path.Combine(directory.FullName, "Directory.Packages.props"));
            bool hasSourceTree = Directory.Exists(Path.Combine(directory.FullName, "Src"));

            if (hasCentralPackages && hasSourceTree)
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException(
            $"Could not locate the repository root from '{AppContext.BaseDirectory}': no ancestor " +
            "directory contains both 'Directory.Packages.props' and 'Src'.");
    }
}

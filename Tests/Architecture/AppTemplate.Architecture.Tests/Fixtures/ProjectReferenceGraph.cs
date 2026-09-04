using System.Xml.Linq;

namespace AppTemplate.Architecture.Tests.Fixtures;

/// <summary>One product project and the projects it declares a reference to.</summary>
/// <param name="Name">The project's file name without its extension, which is also its assembly name.</param>
/// <param name="RelativePath">Path from the repository root, for failure messages.</param>
/// <param name="References">The <c>ProjectReference</c> targets, by project name.</param>
internal sealed record ProjectNode(string Name, string RelativePath, IReadOnlySet<string> References);

/// <summary>
/// The <c>ProjectReference</c> graph of everything under <c>Src</c>, read from the project files.
/// <para>
/// This complements the IL-level rules rather than duplicating them. NetArchTest can only see a
/// dependency that is actually <em>used</em>; a project reference that nothing consumes yet is
/// invisible to it, and is exactly how an inward-pointing arrow gets established one release
/// before anybody depends on it. The declaration is the thing worth constraining, so it is read
/// from the declaration.
/// </para>
/// </summary>
internal static class ProjectReferenceGraph
{
    private const string _infrastructurePrefix = "AppTemplate.Infrastructure.";

    private static readonly string _presentationPrefix =
        Path.Combine("Src", "Presentation") + Path.DirectorySeparatorChar;

    /// <summary>The repository root, located by walking up from the test assembly.</summary>
    internal static string RepositoryRoot { get; } = LocateRepositoryRoot();

    /// <summary>Every project under <c>Src</c>, keyed by project name.</summary>
    internal static IReadOnlyDictionary<string, ProjectNode> SourceProjects { get; } = ReadSourceProjects();

    internal static bool IsInfrastructureModule(string projectName) =>
        projectName.StartsWith(_infrastructurePrefix, StringComparison.Ordinal);

    internal static IEnumerable<ProjectNode> InfrastructureModules =>
        SourceProjects.Values.Where(project => IsInfrastructureModule(project.Name));

    /// <summary>
    /// A composition root: a project under <c>Src\Presentation</c>. Location decides, not name, so a
    /// new host is covered by the rules the moment it exists rather than when somebody lists it.
    /// </summary>
    internal static bool IsHost(ProjectNode project) =>
        project is not null
        && project.RelativePath.StartsWith(_presentationPrefix, StringComparison.Ordinal);

    internal static IEnumerable<ProjectNode> Hosts => SourceProjects.Values.Where(IsHost);

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
            var references = ReadProjectReferences(projectFile);

            projects[name] = new ProjectNode(
                name,
                Path.GetRelativePath(RepositoryRoot, projectFile),
                references);
        }

        if (projects.Count == 0)
        {
            throw new InvalidOperationException(
                $"No project files were found under '{sourceRoot}'. Every rule written against the " +
                "reference graph would pass for the wrong reason.");
        }

        return projects;
    }

    private static HashSet<string> ReadProjectReferences(string projectFile)
    {
        // SDK-style project files carry no XML namespace, so the element name is unqualified.
        var document = XDocument.Load(projectFile);

        return document
            .Descendants("ProjectReference")
            .Select(element => element.Attribute("Include")?.Value)
            .Where(include => !string.IsNullOrWhiteSpace(include))
            .Select(include => Path.GetFileNameWithoutExtension(
                include!.Replace('\\', Path.DirectorySeparatorChar)))
            .ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>
    /// Walks up from the test assembly until it finds the directory that owns Central Package
    /// Management and the product tree. Throws rather than returning a guess: a rule that silently
    /// found no project files would pass.
    /// </summary>
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

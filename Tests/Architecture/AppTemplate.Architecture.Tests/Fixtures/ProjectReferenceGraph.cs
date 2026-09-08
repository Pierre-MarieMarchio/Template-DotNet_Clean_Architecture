using System.Xml.Linq;

namespace AppTemplate.Architecture.Tests.Fixtures;

/// <summary>One product project and the projects it declares a reference to.</summary>
/// <param name="Name">The project's file name without its extension, which is also its assembly name.</param>
/// <param name="RelativePath">Path from the repository root, for failure messages.</param>
/// <param name="References">The <c>ProjectReference</c> targets, by project name.</param>
/// <param name="IsPackable">
/// Whether the project declares <c>IsPackable</c> as true, which is how an SDK project says so.
/// </param>
/// <param name="HasProgram">Whether a <c>Program.cs</c> sits at the project's root.</param>
internal sealed record ProjectNode(
    string Name,
    string RelativePath,
    IReadOnlySet<string> References,
    bool IsPackable,
    bool HasProgram);

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

    /// <summary>The repository root, located by walking up from the test assembly.</summary>
    internal static string RepositoryRoot { get; } = LocateRepositoryRoot();

    /// <summary>Every project under <c>Src</c>, keyed by project name.</summary>
    internal static IReadOnlyDictionary<string, ProjectNode> SourceProjects { get; } = ReadSourceProjects();

    internal static bool IsInfrastructureModule(string projectName) =>
        projectName.StartsWith(_infrastructurePrefix, StringComparison.Ordinal);

    internal static IEnumerable<ProjectNode> InfrastructureModules =>
        SourceProjects.Values.Where(project => IsInfrastructureModule(project.Name));

    /// <summary>
    /// A composition root: a project holding a <c>Program.cs</c> at its root. The entry point
    /// decides, not the location and not the name, so a new host is covered by the rules the moment
    /// it exists rather than when somebody lists it — and a library that shares the presentation
    /// layer with a host is not mistaken for one.
    /// </summary>
    internal static bool IsHost(ProjectNode project) => project is { HasProgram: true };

    internal static IEnumerable<ProjectNode> Hosts => SourceProjects.Values.Where(IsHost);

    /// <summary>
    /// The projects written as if they were packages, recognised by the <c>IsPackable</c> they have
    /// to declare for <c>dotnet pack</c> to produce them. Declaration decides, so the set cannot
    /// drift from what CI actually packs.
    /// </summary>
    internal static IEnumerable<ProjectNode> SdkProjects =>
        SourceProjects.Values.Where(project => project.IsPackable);

    internal static bool IsSdkProject(string projectName) =>
        SourceProjects.TryGetValue(projectName, out var project) && project.IsPackable;

    /// <summary>
    /// The projects of one layer, named by the folder <c>Src</c> groups them under. Location
    /// decides, so a project added to a layer joins every population read from that layer the moment
    /// it exists rather than when somebody lists it.
    /// </summary>
    internal static IEnumerable<ProjectNode> ProjectsInLayer(string layerFolder)
    {
        string prefix = Path.Combine("Src", layerFolder) + Path.DirectorySeparatorChar;

        return SourceProjects.Values
            .Where(project => project.RelativePath.StartsWith(prefix, StringComparison.Ordinal))
            .OrderBy(project => project.Name, StringComparer.Ordinal);
    }

    /// <summary>The directory holding a project file, which is the project's root.</summary>
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
                ReadIsPackable(document),
                File.Exists(Path.Combine(Path.GetDirectoryName(projectFile)!, "Program.cs")));
        }

        if (projects.Count == 0)
        {
            throw new InvalidOperationException(
                $"No project files were found under '{sourceRoot}'. Every rule written against the " +
                "reference graph would pass for the wrong reason.");
        }

        return projects;
    }

    // SDK-style project files carry no XML namespace, so the element names below are unqualified.
    private static HashSet<string> ReadProjectReferences(XDocument document) =>
        document
            .Descendants("ProjectReference")
            .Select(element => element.Attribute("Include")?.Value)
            .Where(include => !string.IsNullOrWhiteSpace(include))
            .Select(include => Path.GetFileNameWithoutExtension(
                include!.Replace('\\', Path.DirectorySeparatorChar)))
            .ToHashSet(StringComparer.Ordinal);

    private static bool ReadIsPackable(XDocument document) =>
        document
            .Descendants("IsPackable")
            .Any(element => bool.TryParse(element.Value.Trim(), out bool packable) && packable);

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

using System.Text.RegularExpressions;

namespace AppTemplate.Architecture.Tests.Fixtures;

internal static class SourceDeclarations
{
    private static readonly Regex _useCaseClassDeclaration = new(
        @"^[\t ]*(?:public|internal)?[\t ]*(?:sealed[\t ]+)?(?:partial[\t ]+)?class[\t ]+([A-Za-z0-9_]+UseCase)\b",
        RegexOptions.Multiline,
        TimeSpan.FromSeconds(5));

    private static readonly Regex _publicInterfaceDeclaration = new(
        @"^[\t ]*public[\t ]+(?:partial[\t ]+)?interface[\t ]+([A-Za-z0-9_]+)",
        RegexOptions.Multiline,
        TimeSpan.FromSeconds(5));

    private static readonly IReadOnlyList<ProjectNode> _applicationProjects =
        [.. ProjectReferenceGraph.ProjectsInLayer("Application")];

    private static readonly IReadOnlyList<ProjectNode> _domainProjects =
        [.. ProjectReferenceGraph.ProjectsInLayer("Domain")];

    internal static IReadOnlyList<string> Verticals { get; } =
        [.. _applicationProjects
            .SelectMany(project => VerticalsOf(project))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)];

    internal static IReadOnlyList<string> UseCaseFullNames { get; }

    internal static IReadOnlyList<string> UseCaseOperationNamespaces { get; }

    internal static IReadOnlyList<string> PortFullNames { get; }

    internal static IReadOnlyList<string> DomainRepositoryFullNames { get; }

    internal static IReadOnlyList<string> WalkComplaints { get; }

    static SourceDeclarations()
    {
        var useCases = _applicationProjects
            .SelectMany(project => DeclarationsIn(
                project,
                [ProjectReferenceGraph.RootOf(project)],
                _useCaseClassDeclaration))
            .ToList();

        UseCaseFullNames = [.. useCases.Order(StringComparer.Ordinal)];

        UseCaseOperationNamespaces =
            [.. useCases
                .Select(fullName => fullName[..fullName.LastIndexOf('.')])
                .Where(@namespace => @namespace.Contains(".UseCases.", StringComparison.Ordinal))
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)];

        PortFullNames =
            [.. _applicationProjects
                .SelectMany(project => DeclarationsIn(
                    project,
                    PortFoldersOf(project),
                    _publicInterfaceDeclaration))
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)];

        DomainRepositoryFullNames =
            [.. _domainProjects
                .SelectMany(project => DeclarationsIn(
                    project,
                    FoldersNamed(ProjectReferenceGraph.RootOf(project), "Repositories"),
                    _publicInterfaceDeclaration))
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)];

        WalkComplaints = ReadWalkComplaints();
    }

    internal static List<string> Divergence(
        IEnumerable<string> inTheSourceTree,
        IEnumerable<string> discovered,
        string discoveredBy)
    {
        var declared = inTheSourceTree.ToHashSet(StringComparer.Ordinal);
        var found = discovered.ToHashSet(StringComparer.Ordinal);

        return
        [
            .. declared.Except(found, StringComparer.Ordinal)
                .Select(name => $"the source tree declares '{name}', which was not {discoveredBy}")
                .Concat(found.Except(declared, StringComparer.Ordinal)
                    .Select(name => $"'{name}' was {discoveredBy}, but the source tree declares no such type there"))
                .Order(StringComparer.Ordinal)
        ];
    }

    internal static string WithoutArity(Type type)
    {
        string fullName = type.FullName ?? type.Name;
        int arity = fullName.IndexOf('`');

        return arity < 0 ? fullName : fullName[..arity];
    }

    private static List<string> ReadWalkComplaints()
    {
        var complaints = new List<string>();

        if (_applicationProjects.Count == 0)
        {
            complaints.Add(
                "The project graph reports no project in the application layer, so every population " +
                "read here is empty and every comparison against one holds by comparing nothing.");
        }

        if (_domainProjects.Count == 0)
        {
            complaints.Add("The project graph reports no project in the domain layer.");
        }

        if (Verticals.Count == 0)
        {
            complaints.Add(
                "No vertical folder was found under the Features of any application project.");
        }

        complaints.AddRange(_applicationProjects
            .SelectMany(project => VerticalsOf(project)
                .Where(vertical => Directory.Exists(
                    Path.Combine(ProjectReferenceGraph.RootOf(project), "Features", vertical, "UseCases")))
                .Where(vertical => !UseCaseFullNames.Any(fullName => fullName.StartsWith(
                    $"{project.Name}.Features.{vertical}.UseCases.", StringComparison.Ordinal)))
                .Select(vertical =>
                    $"'{project.Name}' declares the vertical '{vertical}' with a UseCases folder in " +
                    "which no use-case class declaration was found, so the declaration pattern has " +
                    "stopped matching what is written there.")));

        if (PortFullNames.Count == 0)
        {
            complaints.Add(
                "No port interface was found under a vertical's Ports folder or under the Common " +
                "folder of any application project.");
        }

        //#if (aggregates)
        if (DomainRepositoryFullNames.Count == 0)
        {
            complaints.Add(
                "No repository contract was found in a Repositories folder of any domain project.");
        }
        //#endif

        return complaints;
    }

    private static List<string> VerticalsOf(ProjectNode project) =>
        SubfolderNamesOf(Path.Combine(ProjectReferenceGraph.RootOf(project), "Features"));

    private static IEnumerable<string> PortFoldersOf(ProjectNode project)
    {
        string root = ProjectReferenceGraph.RootOf(project);

        return VerticalsOf(project)
            .Select(vertical => Path.Combine(root, "Features", vertical, "Ports"))
            .Append(Path.Combine(root, "Common"))
            .Where(Directory.Exists);
    }

    private static IEnumerable<string> FoldersNamed(string root, string folderName) =>
        Directory.Exists(root)
            ? Directory.EnumerateDirectories(root, folderName, SearchOption.AllDirectories)
            : [];

    private static List<string> DeclarationsIn(
        ProjectNode project,
        IEnumerable<string> roots,
        Regex declaration)
    {
        string projectRoot = ProjectReferenceGraph.RootOf(project);
        string rootNamespace = project.Name;
        var found = new List<string>();

        foreach (string root in roots.Where(Directory.Exists))
        {
            foreach (string file in SourceFilesUnder(root))
            {
                string @namespace = NamespaceOf(Path.GetDirectoryName(file)!, projectRoot, rootNamespace);

                found.AddRange(declaration
                    .Matches(File.ReadAllText(file))
                    .Select(match => $"{@namespace}.{match.Groups[1].Value}"));
            }
        }

        return found;
    }

    private static string NamespaceOf(string folder, string projectRoot, string rootNamespace)
    {
        string relative = Path.GetRelativePath(projectRoot, folder);

        return string.Equals(relative, ".", StringComparison.Ordinal)
            ? rootNamespace
            : $"{rootNamespace}.{relative.Replace(Path.DirectorySeparatorChar, '.').Replace('/', '.')}";
    }

    private static IEnumerable<string> SourceFilesUnder(string root) =>
        Directory
            .EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(file => !file.Contains(
                $"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(file => !file.Contains(
                $"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal));

    private static List<string> SubfolderNamesOf(string root) =>
        Directory.Exists(root)
            ? [.. Directory
                .EnumerateDirectories(root)
                .Select(folder => new DirectoryInfo(folder).Name)
                .Order(StringComparer.Ordinal)]
            : [];
}

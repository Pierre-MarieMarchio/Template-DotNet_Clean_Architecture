using AppTemplate.Architecture.Tests.Fixtures;
using Shouldly;
using Xunit;

namespace AppTemplate.Architecture.Tests.Rules;

public sealed class TestProjectMirrorTests
{
    private const string _unitTestsSuffix = ".UnitTests";

    private static readonly Dictionary<string, string> _withoutAUnitTestProject = new(StringComparer.Ordinal)
    {
        [".FrameworkSite"] =
            "the WebAssembly app is the output of dotnet new blazorwasm and holds no code of ours; " +
            "the web acceptance legs run it",
    };

    [Fact]
    public void EverySourceProject_HasItsUnitTestProject_ExceptTheOnesNamedHere()
    {
        var sources = ProjectReferenceGraph.SourceProjects.Values
            .Where(project => !project.RelativePath.Contains("node_modules", StringComparison.Ordinal))
            .ToList();

        sources.ShouldNotBeEmpty("No source project was read, so this rule would hold by reading nothing.");

        MissingMirrors(sources, TestProjectPaths(), _withoutAUnitTestProject.Keys)
            .ShouldBeEmpty(
                "A source project has one unit test project, named for it with .UnitTests and placed " +
                "under Tests/ in the same layer folder, as the project layout says. Add the project " +
                "named here; a project that cannot have one goes in the list at the top of this " +
                "class, with its reason.");
    }

    [Fact]
    public void EveryUnitTestProject_MirrorsASourceProject()
    {
        var sourceNames = ProjectReferenceGraph.SourceProjects.Keys.ToHashSet(StringComparer.Ordinal);

        Orphans(TestProjectPaths(), sourceNames)
            .ShouldBeEmpty(
                "A unit test project with no source project of its name tests nothing that exists: " +
                "the project it mirrored was removed or renamed. Delete it, or rename it with its source.");
    }

    [Fact]
    public void TheExceptions_NameNoProjectThatHasAUnitTestProject()
    {
        var paths = TestProjectPaths();

        ProjectReferenceGraph.SourceProjects.Values
            .Where(project => _withoutAUnitTestProject.Keys.Any(suffix => project.Name.EndsWith(suffix, StringComparison.Ordinal)))
            .Where(project => paths.Contains(ExpectedPath(project)))
            .Select(project => project.Name)
            .ShouldBeEmpty("A project in the list of exceptions has a unit test project: take it off the list.");
    }

    [Fact]
    public void TheMirror_IsSensitive_AndSeesAMissingProjectAnOrphanAndAnException()
    {
        string product = ProjectFolders.ProductName;
        var lonely = Source($"Src/Domain/{ProjectFolders.FolderOf(product + ".Domain.Core")}/{product}.Domain.Core.csproj");
        var excepted = Source($"Src/Web/apps/FrameworkSite/{product}.FrameworkSite.csproj");
        string mirror = ExpectedPath(lonely);

        MissingMirrors([lonely, excepted], new HashSet<string>(StringComparer.Ordinal), _withoutAUnitTestProject.Keys)
            .ShouldBe([mirror], "A source project with no test project is reported, and one on the list is not.");

        MissingMirrors([lonely, excepted], new HashSet<string>([mirror], StringComparer.Ordinal), _withoutAUnitTestProject.Keys)
            .ShouldBeEmpty("A source project whose mirror exists is not reported.");

        Orphans(
                [$"Tests/Domain/{ProjectFolders.FolderOf(product + ".Domain.Gone")}.UnitTests/{product}.Domain.Gone.UnitTests.csproj"],
                new HashSet<string>([product + ".Domain.Core"], StringComparer.Ordinal))
            .ShouldNotBeEmpty("A test project whose source does not exist is reported.");
    }

    private static List<string> MissingMirrors(
        IEnumerable<ProjectNode> sources,
        HashSet<string> testProjectPaths,
        IEnumerable<string> exceptions) =>
        [
            .. sources
                .Where(project => !exceptions.Any(suffix => project.Name.EndsWith(suffix, StringComparison.Ordinal)))
                .Select(ExpectedPath)
                .Where(path => !testProjectPaths.Contains(path))
                .Order(StringComparer.Ordinal),
        ];

    private static List<string> Orphans(IEnumerable<string> testProjectPaths, HashSet<string> sourceNames) =>
        [
            .. testProjectPaths
                .Where(path => Path.GetFileNameWithoutExtension(path).EndsWith(_unitTestsSuffix, StringComparison.Ordinal))
                .Where(path => !sourceNames.Contains(
                    Path.GetFileNameWithoutExtension(path)[..^_unitTestsSuffix.Length]))
                .Order(StringComparer.Ordinal),
        ];

    private static string ExpectedPath(ProjectNode source)
    {
        string[] segments = source.RelativePath.Replace('\\', '/').Split('/');

        return $"Tests/{segments[1]}/{ProjectFolders.FolderOf(source.Name)}{_unitTestsSuffix}/{source.Name}{_unitTestsSuffix}.csproj";
    }

    private static ProjectNode Source(string relativePath) =>
        new(
            Path.GetFileNameWithoutExtension(relativePath),
            relativePath,
            new HashSet<string>(),
            false,
            false,
            string.Empty,
            new HashSet<string>(),
            new HashSet<string>());

    private static HashSet<string> TestProjectPaths()
    {
        string root = ProjectReferenceGraph.RepositoryRoot;

        return
        [
            .. Directory.EnumerateFiles(Path.Combine(root, "Tests"), "*.csproj", SearchOption.AllDirectories)
                .Select(path => Path.GetRelativePath(root, path).Replace('\\', '/'))
                .Where(path => !path.Contains("/node_modules/", StringComparison.Ordinal)),
        ];
    }
}

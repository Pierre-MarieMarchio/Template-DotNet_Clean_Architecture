using AppTemplate.Architecture.Tests.Fixtures;
using Shouldly;
using Xunit;

namespace AppTemplate.Architecture.Tests.Rules;

public sealed class TestProjectMirrorTests
{
    private const string _unitTestsSuffix = ".UnitTests";

    private static readonly MirrorException[] _withoutAUnitTestProject =
    [
        new(
            "a project of the web workspace is the output of an official CLI (dotnet new blazorwasm for a " +
            "WebAssembly app) and the web acceptance legs run it; one that brings its own unit tests keeps them",
            (project, _) => project.RelativePath.Replace('\\', '/').StartsWith("Src/Web/", StringComparison.Ordinal)),
        new(
            "a host written beside its <Name>.Core keeps its logic there, and <Name>.Core.UnitTests tests it; " +
            "the host itself is views and composition (a WPF host builds on Windows only)",
            (project, sourceNames) => sourceNames.Contains(project.Name + ".Core")),
        //#if (aspire)
        new(
            "the app host composes the local stack and none of the product: LocalStackTests in its integration " +
            "project starts it, and LocalStackParityTests holds it to the Compose files",
            (project, _) => project.RelativePath.Replace('\\', '/').StartsWith("Src/AppHost/", StringComparison.Ordinal)),
        //#endif
    ];

    [Fact]
    public void EverySourceProject_HasItsUnitTestProject_ExceptTheOnesNamedHere()
    {
        var sources = ProjectReferenceGraph.SourceProjects.Values
            .Where(project => !project.RelativePath.Contains("node_modules", StringComparison.Ordinal))
            .ToList();

        sources.ShouldNotBeEmpty("No source project was read, so this rule would hold by reading nothing.");

        MissingMirrors(sources, TestProjectPaths(), SourceNames())
            .ShouldBeEmpty(
                "A source project has one unit test project, named for it with .UnitTests and placed " +
                "under Tests/ in the same layer folder, as the project layout says. Add the project " +
                "named here; a kind of project that cannot have one goes in the list at the top of " +
                "this class, with its reason.");
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
    public void TheMirror_IsSensitive_AndSeesAMissingProjectAnOrphanAndTheExceptions()
    {
        string product = ProjectFolders.ProductName;
        var lonely = Source($"Src/Domain/{ProjectFolders.FolderOf(product + ".Domain.Core")}/{product}.Domain.Core.csproj");
        var webApp = Source($"Src/Web/apps/AnySite/{product}.AnySite.csproj");
        var hostWithCore = Source($"Src/Presentation/{ProjectFolders.FolderOf(product + ".Desktop")}/{product}.Desktop.csproj");
        string mirror = ExpectedPath(lonely);
        HashSet<string> none = new(StringComparer.Ordinal);

        MissingMirrors([lonely, webApp, hostWithCore], none, new HashSet<string>([product + ".Desktop.Core"], StringComparer.Ordinal))
            .ShouldBe([mirror], "A source project with no test project is reported; a web app and a host beside its .Core are not.");

        MissingMirrors([hostWithCore], none, none)
            .ShouldBe([ExpectedPath(hostWithCore)], "A host with no .Core beside it needs its own unit test project.");

        MissingMirrors([lonely], new HashSet<string>([mirror], StringComparer.Ordinal), none)
            .ShouldBeEmpty("A source project whose mirror exists is not reported.");

        Orphans(
                [$"Tests/Domain/{ProjectFolders.FolderOf(product + ".Domain.Gone")}.UnitTests/{product}.Domain.Gone.UnitTests.csproj"],
                new HashSet<string>([product + ".Domain.Core"], StringComparer.Ordinal))
            .ShouldNotBeEmpty("A test project whose source does not exist is reported.");
    }

    private static List<string> MissingMirrors(
        IEnumerable<ProjectNode> sources,
        HashSet<string> testProjectPaths,
        HashSet<string> sourceNames) =>
        [
            .. sources
                .Where(project => !Array.Exists(_withoutAUnitTestProject, exception => exception.Applies(project, sourceNames)))
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

    private static HashSet<string> SourceNames() =>
        ProjectReferenceGraph.SourceProjects.Keys.ToHashSet(StringComparer.Ordinal);

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

    private sealed record MirrorException(string Reason, Func<ProjectNode, HashSet<string>, bool> Applies);
}

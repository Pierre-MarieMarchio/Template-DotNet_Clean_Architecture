using AppTemplate.Architecture.Tests.Fixtures;
using Shouldly;
using Xunit;

namespace AppTemplate.Architecture.Tests.Rules;

public sealed class ProjectFolderTests
{
    private const string _webWorkspace = "Src/Web/";

    private static readonly string[] _trees = ["Src", "Tests"];

    private static readonly HashSet<string> _skippedFolders = new(StringComparer.Ordinal)
    {
        "bin", "obj", "node_modules", "TestResults",
    };

    [Fact]
    public void EveryProject_SitsInTheFolderItsNameGives()
    {
        string[] projects = [.. ProjectFiles()];

        projects.Length.ShouldBeGreaterThanOrEqualTo(10, "Far fewer project files were found under Src and Tests than this project holds, so this rule is not reading its tree.");

        projects.Select(Misplacement).OfType<string>().ShouldBeEmpty(
            $"A project sits in <Src|Tests>/<layer>/<folder>/, its folder being its name with the product's name, {ProjectFolders.ProductName}, "
            + $"replaced by the product's initials, {ProjectFolders.InitialsOf(ProjectFolders.ProductName)}: move each project named here and update the paths that cite it.");
    }

    private static string? Misplacement(string relative)
    {
        string name = Path.GetFileNameWithoutExtension(relative);

        if (!name.StartsWith(ProjectFolders.ProductName + ".", StringComparison.Ordinal))
        {
            return $"{relative}: its name does not start with {ProjectFolders.ProductName}., as every project's name does.";
        }

        string[] segments = relative.Split('/');
        string layer = segments.Length > 2 ? segments[1] : "<layer>";
        string folder = $"{segments[0]}/{layer}/{ProjectFolders.FolderOf(name)}";

        return relative == $"{folder}/{name}.csproj" ? null : $"{relative} belongs in {folder}/";
    }

    private static IEnumerable<string> ProjectFiles() =>
        _trees
            .Select(tree => Path.Combine(ProjectReferenceGraph.RepositoryRoot, tree))
            .Where(Directory.Exists)
            .SelectMany(ProjectFilesUnder)
            .Select(path => Path.GetRelativePath(ProjectReferenceGraph.RepositoryRoot, path).Replace('\\', '/'))
            .Where(relative => !relative.StartsWith(_webWorkspace, StringComparison.Ordinal))
            .Order(StringComparer.Ordinal);

    private static IEnumerable<string> ProjectFilesUnder(string directory) =>
        Directory.EnumerateFiles(directory, "*.csproj")
            .Concat(Directory.EnumerateDirectories(directory)
                .Where(child => !_skippedFolders.Contains(Path.GetFileName(child)))
                .SelectMany(ProjectFilesUnder));

    [Fact]
    public void TheRule_IsSensitive_AndSeesAMisplacedAndAMisnamedProject()
    {
        string product = ProjectFolders.ProductName;
        string name = $"{product}.Domain";
        string folder = ProjectFolders.FolderOf(name);

        Misplacement($"Src/Domain/{folder}/{name}.csproj").ShouldBeNull();
        Misplacement($"Src/Domain/Elsewhere/{name}.csproj").ShouldBe($"Src/Domain/Elsewhere/{name}.csproj belongs in Src/Domain/{folder}/");
        Misplacement("Src/Domain/Other/Other.Domain.csproj").ShouldNotBeNull().ShouldContain("does not start with");
    }

    [Theory]
    [InlineData("Acme.Shop", "AS")]
    [InlineData("AcmeShop", "AS")]
    [InlineData("Acme", "A")]
    [InlineData("acme.shop", "AS")]
    public void TheInitials_AreSensitive_AndTakeCapitalsOrTheFirstLetter(string product, string initials) =>
        ProjectFolders.InitialsOf(product).ShouldBe(initials);
}

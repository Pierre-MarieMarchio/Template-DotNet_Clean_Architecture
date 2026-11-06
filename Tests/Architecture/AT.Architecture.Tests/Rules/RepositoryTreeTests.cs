using AppTemplate.Architecture.Tests.Fixtures;
using Shouldly;
using Xunit;

namespace AppTemplate.Architecture.Tests.Rules;

public sealed class RepositoryTreeTests
{
    [Fact]
    public void TheSharedWalk_IsSensitive_AndSkipsOnlyTheFoldersItIsGiven()
    {
        string root = Path.Combine(Path.GetTempPath(), $"{nameof(RepositoryTreeTests)}.{Guid.NewGuid():N}");

        string[] files =
        [
            Path.Combine(root, "Top.cs"),
            Path.Combine(root, "Src", "Kept.cs"),
            Path.Combine(root, "Src", "Kept.md"),
            Path.Combine(root, "Src", "obj", "Skipped.cs"),
            Path.Combine(root, "Src", "web", "node_modules", "Skipped.cs"),
            Path.Combine(root, "Src", "bin", "Deep", "Skipped.cs"),
        ];

        try
        {
            foreach (string file in files)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(file)!);
                File.WriteAllText(file, string.Empty);
            }

            RepositoryTree.SourceFiles(root, "*.cs", ["bin", "obj", "node_modules"])
                .Select(file => RepositoryTree.Relative(root, file))
                .ShouldBe(["Top.cs", "Src/Kept.cs"], ignoreOrder: true);

            RepositoryTree.SourceFiles(root, "*.cs", ["obj"])
                .Select(file => RepositoryTree.Relative(root, file))
                .ShouldContain("Src/bin/Deep/Skipped.cs");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}

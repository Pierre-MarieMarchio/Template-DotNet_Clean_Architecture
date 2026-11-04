using AppTemplate.Architecture.Tests.Fixtures;
using Shouldly;
using Xunit;

namespace AppTemplate.Architecture.Tests.Rules;

public sealed class MarkdownEncodingTests
{
    private static readonly string[] _skippedDirectories = ["bin", "obj", "node_modules", "plans"];

    private static readonly string[] _keptDotDirectories = [".github", ".template.config"];

    [Fact]
    public void EveryMarkdownFile_IsUtf8WithoutABom_WithLfEndings()
    {
        string root = ProjectReferenceGraph.RepositoryRoot;

        string[] refused = [.. MarkdownFiles(root)
            .Where(file => Problem(File.ReadAllBytes(file)) is not null)
            .Select(file => $"{Path.GetRelativePath(root, file).Replace('\\', '/')}: {Problem(File.ReadAllBytes(file))}")
            .Order(StringComparer.Ordinal)];

        refused.ShouldBeEmpty(
            "a page's frontmatter starts at its first byte, and a reader that does not expect a BOM or a CR "
            + "reads neither as Markdown; `dotnet run Tools/Tasks.cs format-fix` removes both.");
    }

    [Fact]
    public void TheRule_RefusesABomAndACarriageReturn_AndAcceptsPlainUtf8()
    {
        Problem([0xEF, 0xBB, 0xBF, (byte)'#', (byte)'\n']).ShouldNotBeNull();
        Problem([(byte)'#', (byte)'\r', (byte)'\n']).ShouldNotBeNull();
        Problem([(byte)'#', (byte)' ', 0xC3, 0xA9, (byte)'\n']).ShouldBeNull();
        Problem([]).ShouldBeNull();
    }

    private static string? Problem(byte[] content) =>
        content is [0xEF, 0xBB, 0xBF, ..] ? "starts with a UTF-8 byte order mark"
        : content.Contains((byte)'\r') ? "holds a CR line ending"
        : null;

    private static IEnumerable<string> MarkdownFiles(string directory) =>
        Directory.EnumerateFiles(directory, "*.md")
            .Concat(Directory.EnumerateDirectories(directory)
                .Where(child => Path.GetFileName(child) is var name
                    && !_skippedDirectories.Contains(name, StringComparer.Ordinal)
                    && (!name.StartsWith('.') || _keptDotDirectories.Contains(name, StringComparer.Ordinal)))
                .SelectMany(MarkdownFiles));
}

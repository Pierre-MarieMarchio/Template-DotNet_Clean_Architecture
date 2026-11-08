using CleanArch.Tooling.Markdown;
using Shouldly;
using Xunit;

namespace CleanArch.Tooling.Tests.Markdown;

public sealed class MarkdownSectionTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("cleanarch-tooling-").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void Section_EndsAtTheNextHeadingOfItsLevel_NotAtACommentInACodeBlock()
    {
        string readme = Path.Combine(_root, "README.md");
        File.WriteAllText(
            readme,
            """
            # Title

            ## Join the stack

            ```yaml
            # One line per web app
            include:
            ```

            ### Detail

            ## Next
            """);

        string section = MarkdownSection.Of(readme, "## Join the stack").ShouldNotBeNull();

        section.ShouldContain("# One line per web app");
        section.ShouldContain("### Detail");
        section.ShouldNotContain("## Next");
    }
}

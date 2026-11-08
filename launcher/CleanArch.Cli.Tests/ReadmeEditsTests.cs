using CleanArch.Cli.Joins;
using Shouldly;
using Xunit;

namespace CleanArch.Cli.Tests;

public sealed class ReadmeEditsTests
{
    [Fact]
    public void Parse_AnEditWhoseLinesHoldAFencedBlock_KeepsTheBlockWhole()
    {
        string[] section =
        [
            "- in `docs/page.md`, after the heading:",
            "",
            "  ```text",
            "  # Page",
            "  ```",
            "",
            "  ````markdown",
            "  Run:",
            "  ```bash",
            "  dotnet cleanarch check",
            "  ```",
            "  Done.",
            "  ````",
            "",
            "- in `docs/other.md`, remove the line:",
            "",
            "  ```text",
            "  gone",
            "  ```",
        ];

        List<ReadmeEdit> edits = ReadmeEdits.Parse(section, "README.md");

        edits.Count.ShouldBe(2);
        edits[0].Anchor.ShouldBe(["# Page"]);
        edits[0].Lines.ShouldBe(["Run:", "```bash", "dotnet cleanarch check", "```", "Done."]);
        edits[1].Anchor.ShouldBe(["gone"]);
    }
}

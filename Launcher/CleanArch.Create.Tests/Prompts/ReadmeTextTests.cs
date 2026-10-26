using CleanArch.Create.Prompts;
using Shouldly;
using Xunit;

namespace CleanArch.Create.Tests.Prompts;

public sealed class ReadmeTextTests
{
    private const string _runIt =
        """
        You need the .NET SDK `global.json` pins and Docker with Compose v2.

        ```bash
        docker compose up -d --build --wait
        ```

        | Service | Address |
        |---|---|
        | API | <http://localhost:8080> |
        | Scalar, the OpenAPI UI | <http://localhost:8080/scalar/v1> |

        [First run](docs/getting-started/first-run.md) continues from there.
        """;

    [Fact]
    public void TheReadmeSection_ReadsAsText_WithoutItsMarkdown()
    {
        string text = ReadmeText.Plain(_runIt);

        text.ShouldContain("You need the .NET SDK global.json pins and Docker with Compose v2.");
        text.ShouldContain("docker compose up -d --build --wait");
        text.ShouldContain("First run (docs/getting-started/first-run.md) continues from there.");
        text.ShouldNotContain("```");
        text.ShouldNotContain("`");
        text.ShouldNotContain("|");
        text.ShouldNotContain("<http");
    }

    [Fact]
    public void ATable_IsAlignedInColumns_WithoutItsSeparatorRow()
    {
        string[] lines = ReadmeText.Plain(_runIt).Split('\n');

        lines.ShouldContain("Service                 Address");
        lines.ShouldContain("API                     http://localhost:8080");
        lines.ShouldContain("Scalar, the OpenAPI UI  http://localhost:8080/scalar/v1");
    }

    [Fact]
    public void ACommand_KeepsItsCodeStyle_AndAParagraphJoinsItsLines()
    {
        var blocks = ReadmeText.Blocks("A line\nwrapped here.\n\n```bash\ncp .env.example .env\n```").ToList();

        blocks.ShouldContain(("A line wrapped here.", false));
        blocks.ShouldContain(("cp .env.example .env", true));
    }
}

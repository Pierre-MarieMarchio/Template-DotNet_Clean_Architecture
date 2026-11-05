using System.Text.Json.Nodes;
using Shouldly;
using Xunit;

namespace CleanArch.Cli.Tests;

public sealed class FormatCommandTests : IDisposable
{
    private readonly StringWriter _output = new();
    private readonly StringWriter _error = new();

    [Fact]
    public void Format_RepairsTheTreeAndPrintsEachFileItSaves()
    {
        using var project = new SampleProject(SampleProject.Minimal);
        string unsorted = Path.Combine(project.Folder("Src"), "Unsorted.cs");
        File.WriteAllBytes(unsorted, "using Xunit;\r\nusing System.Text;\r\n\r\nnamespace Acme;\r\n"u8.ToArray());
        string readme = Path.Combine(project.Root, "README.md");
        byte[] unformatted = [0xEF, 0xBB, 0xBF, .. "# Hi\r\n"u8.ToArray()];
        File.WriteAllBytes(readme, unformatted);

        Run(["format"], project.Root).ShouldBe(CleanArchCommand.Succeeded);

        _output.ToString().ShouldContain("Src/Unsorted.cs: saved as UTF-8 with a BOM, with LF endings, usings ordered, System first.");
        _output.ToString().ShouldContain("README.md: saved as UTF-8 without a BOM, with LF endings.");
        _error.ToString().ShouldBeEmpty();
        File.ReadAllBytes(unsorted).ShouldBe([0xEF, 0xBB, 0xBF, .. "using System.Text;\nusing Xunit;\n\nnamespace Acme;\n"u8.ToArray()]);
        File.ReadAllBytes(readme).ShouldBe("# Hi\n"u8.ToArray());
    }

    [Fact]
    public void Format_OrdersUsingsSystemFirst()
    {
        using var project = new SampleProject(SampleProject.Minimal);
        string file = Path.Combine(project.Root, "Acme.cs");
        File.WriteAllBytes(file, [0xEF, 0xBB, 0xBF, .. "using Acme;\nusing System;\nusing System.Text;\n\nnamespace Acme;\n"u8.ToArray()]);

        Run(["format"], project.Root).ShouldBe(CleanArchCommand.Succeeded);

        File.ReadAllBytes(file).ShouldBe([0xEF, 0xBB, 0xBF, .. "using System;\nusing System.Text;\nusing Acme;\n\nnamespace Acme;\n"u8.ToArray()]);
        _output.ToString().ShouldContain("Acme.cs: saved as UTF-8 with a BOM, with LF endings, usings ordered, System first.");
        _error.ToString().ShouldBeEmpty();
    }

    [Fact]
    public void Format_LeavesACleanTreeAlone()
    {
        using var project = new SampleProject(SampleProject.Minimal);
        string file = Path.Combine(project.Root, "Program.cs");
        byte[] clean = [0xEF, 0xBB, 0xBF, .. "using System;\n\nnamespace Acme;\n"u8.ToArray()];
        File.WriteAllBytes(file, clean);

        Run(["format"], project.Root).ShouldBe(CleanArchCommand.Succeeded);

        _output.ToString().ShouldBeEmpty();
        _error.ToString().ShouldBeEmpty();
        File.ReadAllBytes(file).ShouldBe(clean);
    }

    [Fact]
    public void Format_SkipsTheFoldersTheTaskSkips()
    {
        using var project = new SampleProject(SampleProject.Minimal);
        string generated = Path.Combine(project.Folder("obj"), "Generated.cs");
        File.WriteAllBytes(generated, "namespace Acme;\r\n"u8.ToArray());
        string hook = Path.Combine(project.Folder(".git"), "hook.md");
        File.WriteAllBytes(hook, [0xEF, 0xBB, 0xBF, .. "# Hi\r\n"u8.ToArray()]);

        Run(["format"], project.Root).ShouldBe(CleanArchCommand.Succeeded);

        _output.ToString().ShouldBeEmpty();
        _error.ToString().ShouldBeEmpty();
        File.ReadAllBytes(generated).ShouldBe("namespace Acme;\r\n"u8.ToArray());
        File.ReadAllBytes(hook).ShouldBe([0xEF, 0xBB, 0xBF, .. "# Hi\r\n"u8.ToArray()]);
    }

    [Fact]
    public void FormatCheck_FailsWhereItWouldWrite_AndWritesNothing()
    {
        using var project = new SampleProject(SampleProject.Minimal);
        string unsorted = Path.Combine(project.Folder("Src"), "Unsorted.cs");
        byte[] cs = "using Xunit;\r\nusing System.Text;\r\n\r\nnamespace Acme;\r\n"u8.ToArray();
        File.WriteAllBytes(unsorted, cs);
        string readme = Path.Combine(project.Root, "README.md");
        byte[] markdown = [0xEF, 0xBB, 0xBF, .. "# Hi\r\n"u8.ToArray()];
        File.WriteAllBytes(readme, markdown);

        Run(["format", "--check"], project.Root).ShouldBe(CleanArchCommand.Failed);

        _error.ToString().ShouldContain("Src/Unsorted.cs: would be saved as UTF-8 with a BOM, with LF endings, usings ordered, System first.");
        _error.ToString().ShouldContain("README.md: would be saved as UTF-8 without a BOM, with LF endings.");
        _error.ToString().ShouldContain("run it without --check");
        _output.ToString().ShouldBeEmpty();
        File.ReadAllBytes(unsorted).ShouldBe(cs);
        File.ReadAllBytes(readme).ShouldBe(markdown);
    }

    [Fact]
    public void FormatCheck_PassesWhenNothingWouldBeWritten()
    {
        using var project = new SampleProject(SampleProject.Minimal);
        File.WriteAllBytes(Path.Combine(project.Root, "README.md"), "# Hi\n"u8.ToArray());

        Run(["format", "--check"], project.Root).ShouldBe(CleanArchCommand.Succeeded);

        _output.ToString().ShouldBeEmpty();
        _error.ToString().ShouldBeEmpty();
    }

    [Fact]
    public void Format_ThenFormatCheck_RepairsThenPasses()
    {
        using var project = new SampleProject(SampleProject.Minimal);
        string readme = Path.Combine(project.Root, "README.md");
        File.WriteAllBytes(readme, [0xEF, 0xBB, 0xBF, .. "# Hi\r\n"u8.ToArray()]);

        Run(["format"], project.Root).ShouldBe(CleanArchCommand.Succeeded);
        _output.GetStringBuilder().Clear();
        Run(["format", "--check"], project.Root).ShouldBe(CleanArchCommand.Succeeded);

        _output.ToString().ShouldBeEmpty();
        _error.ToString().ShouldBeEmpty();
        File.ReadAllBytes(readme).ShouldBe("# Hi\n"u8.ToArray());
    }

    [Fact]
    public void Format_RefusesOutsideAProject()
    {
        string folder = Directory.CreateTempSubdirectory("cleanarch-cli-").FullName;
        try
        {
            Run(["format"], folder).ShouldBe(CleanArchCommand.Failed);
        }
        finally
        {
            Directory.Delete(folder);
        }

        _error.ToString().ShouldStartWith("Not inside a CleanArch project");
        _error.ToString().ShouldContain(ProjectLocator.FileName);
        _output.ToString().ShouldBeEmpty();
    }

    [Fact]
    public void Format_RefusesAProjectFileItCannotRead()
    {
        using var project = new SampleProject("{ not json");

        Run(["format"], project.Root).ShouldBe(CleanArchCommand.Failed);

        _error.ToString().ShouldContain(Path.Combine(project.Root, ProjectLocator.FileName));
        _output.ToString().ShouldBeEmpty();
    }

    [Fact]
    public void Help_ListsFormatInsideAProject_WithItsCheckOption()
    {
        using var project = new SampleProject(SampleProject.Minimal);

        Run(["--help"], project.Root).ShouldBe(CleanArchCommand.Succeeded);
        _output.ToString().ShouldContain("  format  ");
        _output.GetStringBuilder().Clear();

        Run(["format", "--help"], project.Root).ShouldBe(CleanArchCommand.Succeeded);

        _output.ToString().ShouldContain("dotnet cleanarch format [options]");
        _output.ToString().ShouldContain("--check");
        _error.ToString().ShouldBeEmpty();
    }

    [Fact]
    public void Help_OmitsFormatOutsideAProject()
    {
        Run(["--help"], Path.GetTempPath()).ShouldBe(CleanArchCommand.Succeeded);

        _output.ToString().ShouldNotContain("Repair the tree");
        _error.ToString().ShouldBeEmpty();
    }

    [Fact]
    public void HelpJson_ListsFormatWithItsCheckOption()
    {
        using var project = new SampleProject(SampleProject.Full);

        Run(["help", "--json"], project.Root).ShouldBe(CleanArchCommand.Succeeded);

        JsonObject document = JsonNode.Parse(_output.ToString()).ShouldNotBeNull().AsObject();
        JsonArray commands = document["commands"].ShouldNotBeNull().AsArray();
        commands.Select(command => Text(command!["name"])).ShouldBe(["help", "format"]);
        JsonObject format = commands.Single(command => Text(command!["name"]) == "format").ShouldNotBeNull().AsObject();
        format["options"].ShouldNotBeNull().AsArray().Select(option => Text(option!["name"]))
            .ShouldBe(["--check"]);
        _error.ToString().ShouldBeEmpty();
    }

    public void Dispose()
    {
        _output.Dispose();
        _error.Dispose();
    }

    private int Run(string[] arguments, string workingDirectory) =>
        CleanArchCommand.Run(arguments, workingDirectory, _output, _error);

    private static string Text(JsonNode? node) => node.ShouldNotBeNull().GetValue<string>();
}

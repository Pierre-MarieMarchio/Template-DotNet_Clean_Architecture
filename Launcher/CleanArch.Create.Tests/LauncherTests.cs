using Shouldly;
using Spectre.Console.Testing;
using Xunit;

namespace CleanArch.Create.Tests;

public sealed class LauncherTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("cleanarch-create-").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void MarkdownSection_EndsAtTheNextHeadingOfItsLevel_NotAtACommentInACodeBlock()
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

        string section = Launcher.MarkdownSection(readme, "## Join the stack").ShouldNotBeNull();

        section.ShouldContain("# One line per web app");
        section.ShouldContain("### Detail");
        section.ShouldNotContain("## Next");
    }

    [Fact]
    public void TheWebAppReadmeSections_TheLauncherPrints_AreInTheTemplate()
    {
        Launcher.MarkdownSection(Path.Combine(Repository.Root, "Src", "Web", "README.md"), "## The first app of a project generated without one")
            .ShouldNotBeNullOrWhiteSpace();
        Launcher.MarkdownSection(Path.Combine(Repository.Root, "Src", "Web", "apps", "PlaceholderSite", "README.md"), "## Join the stack")
            .ShouldNotBeNullOrWhiteSpace();
        Launcher.MarkdownSection(Path.Combine(Repository.Root, ".template.config", "variants", "project", "README.md"), "## Run it")
            .ShouldNotBeNullOrWhiteSpace();
    }

    [Theory]
    [InlineData("web-app")]
    [InlineData("web-package")]
    public void AnItemCommand_OutsideAProject_IsRefused(string command)
    {
        var console = new TestConsole();
        console.Profile.Width = Terminal.UnwrappedWidth;

        new Launcher(console, _root, interactive: false).Run([command, "-n", "Shop", "--yes"]).ShouldBe(1);

        console.Output.ShouldContain("only works inside a project made with this template");
    }

    [Fact]
    public void TheHelp_ListsTheTemplatesOptions_WithTheValuesItGenerates()
    {
        var console = new TestConsole();
        console.Profile.Width = Terminal.UnwrappedWidth;

        new Launcher(console, _root, interactive: false).Run(["--help"]).ShouldBe(0);

        console.Output.ShouldContain("--auth local|external|none");
        console.Output.ShouldContain("--database postgresql|sqlserver|sqlite ");
        console.Output.ShouldContain("Authentication (default: local)");
    }

    [Fact]
    public void WithoutQuestions_TheCommandPrinted_IsDotnetNews_WithTheHiveGiven()
    {
        var console = new TestConsole();
        console.Profile.Width = Terminal.UnwrappedWidth;

        new Launcher(console, _root, interactive: false, new FakeProcesses())
            .Run(["new", "-n", "Acme.Shop", "--yes", "--template-source", Repository.Root, "--hive", "hive"])
            .ShouldBe(0, console.Output);

        console.Output.ShouldNotContain(Launcher.Invocation);
        console.Output.ShouldContain("dotnet new cleanarch-webapi -n Acme.Shop -o Acme.Shop --debug:custom-hive hive");
    }

    [Fact]
    public void AnExistingOutputFolder_WithFiles_IsRefusedBeforeAnythingIsInstalled()
    {
        File.WriteAllText(Path.Combine(_root, "kept.txt"), string.Empty);
        var console = new TestConsole();
        console.Profile.Width = Terminal.UnwrappedWidth;

        new Launcher(console, _root, interactive: false).Run(["new", "-n", "Acme.Shop", "-o", ".", "--yes"]).ShouldBe(1);

        console.Output.ShouldContain("already has files in it");
    }
}

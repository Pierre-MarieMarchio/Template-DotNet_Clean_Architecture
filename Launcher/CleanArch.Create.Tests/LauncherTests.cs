using CleanArch.Tooling;
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
        Launcher.MarkdownSection(Path.Combine(Repository.Root, "Src", "Web", "README.md"), Launcher.JoiningSection)
            .ShouldNotBeNull().ShouldContain(Launcher.WebJoinTask);
        Launcher.MarkdownSection(Path.Combine(Repository.Root, "Src", "Web", "apps", "FrameworkSite", "README.md"), "## Join the stack")
            .ShouldNotBeNullOrWhiteSpace();
        Launcher.MarkdownSection(Path.Combine(Repository.Root, "Src", "Web", "apps", "PlaceholderSite", "README.md"), "## Join the stack")
            .ShouldNotBeNullOrWhiteSpace();
        Launcher.MarkdownSection(Path.Combine(Repository.Root, ".template.config", "variants", "project", "README.md"), "## Run it")
            .ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public void ThePresentationReadmeSections_TheLauncherPrints_AreInTheStarter()
    {
        string readme = Path.Combine(Repository.Root, ".template.config", "starters", "blazor-server", "Src", "Presentation", "AT.PresentationAppPortal", "README.md");

        Launcher.MarkdownSection(readme, Launcher.JoiningSection).ShouldNotBeNull().ShouldContain(Launcher.PresentationJoinTask);
        Launcher.MarkdownSection(readme, Launcher.PresentationCreateSection).ShouldNotBeNull().ShouldContain(Launcher.PresentationScaffoldTask);
    }

    [Fact]
    public void TheWebAssemblyAppReadmeSection_TheLauncherJoinsBy_IsInTheStarter()
    {
        string readme = Path.Combine(Repository.Root, ".template.config", "starters", "blazor-wasm", "FrameworkSite", "README.md");

        Launcher.MarkdownSection(readme, Launcher.JoiningSection).ShouldNotBeNull().ShouldContain(Launcher.WebJoinTask);
    }

    [Fact]
    public void TheTasks_TheLauncherRuns_AreTasksOfTheProject()
    {
        string tasks = File.ReadAllText(Path.Combine(Repository.Root, "Tools", "Tasks.cs"));

        tasks.ShouldContain($"case \"{Launcher.PresentationJoinTask}\":");
        tasks.ShouldContain($"case \"{Launcher.PresentationScaffoldTask}\":");
        tasks.ShouldContain($"case \"{Launcher.WebJoinTask}\":");
    }

    [Theory]
    [InlineData("web-app")]
    [InlineData("web-package")]
    [InlineData("presentation")]
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
        console.Output.ShouldContain("--presentation api|blazor-server|mcp|avalonia|wpf|worker");
        console.Output.ShouldContain("Presentation projects, one --presentation per value (default: api)");
    }

    [Theory]
    [InlineData("web-app", "--framework placeholder|angular", "--has-client")]
    [InlineData("web-package", "web-package [options]", "--scope")]
    [InlineData("presentation", "--framework blazor-server|avalonia|mcp|wpf|worker", "--has-api")]
    public void TheHelpOfACommand_ShowsThatCommandsOptions_NotTheProjects(string command, string shown, string alsoShown)
    {
        var console = new TestConsole();
        console.Profile.Width = Terminal.UnwrappedWidth;

        new Launcher(console, _root, interactive: false).Run([command, "--help"]).ShouldBe(0, console.Output);

        console.Output.ShouldContain($"Usage: {Launcher.InvocationWithArguments} {command} ");
        console.Output.ShouldContain(shown);
        console.Output.ShouldContain(alsoShown);
        console.Output.ShouldNotContain("--presentation api|");
        console.Output.ShouldNotContain("--output <folder>");
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

    [Theory]
    [InlineData("--examples", "all", "add --examples none")]
    [InlineData("--files", "true", "add --files false")]
    public void ARefusedCombination_IsExplainedInWordsWithItsFix_AndLeavesNoFolderBehind(string option, string value, string fix)
    {
        var console = new TestConsole();
        console.Profile.Width = Terminal.UnwrappedWidth;
        var processes = new FakeProcesses();

        new Launcher(console, _root, interactive: false, processes)
            .Run(["new", "-n", "Acme.Shop", "--yes", "--template-source", Repository.Root, "--auth", "none", option, value])
            .ShouldBe(1);

        console.Output.ShouldContain(fix);
        console.Output.ShouldContain("Nothing was written");
        console.Output.ShouldNotContain("auth-none-needs");
        console.Output.ShouldNotContain("refused by --");
        Directory.Exists(Path.Combine(_root, "Acme.Shop")).ShouldBeFalse();
        processes.Calls.ShouldBeEmpty();
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

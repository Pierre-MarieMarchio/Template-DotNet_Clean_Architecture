using CleanArch.Create.Hosting;
using CleanArch.Tooling.Output;
using Shouldly;
using Spectre.Console.Testing;
using Xunit;

namespace CleanArch.Create.Tests.Hosting;

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

    [Theory]
    [InlineData("w", false)]
    [InlineData("W", false)]
    [InlineData("Wx", true)]
    [InlineData("AB", true)]
    public void AProjectName_UnderTwoCharacters_IsRefusedWithTheMinimum_BeforeAnythingRuns(string name, bool generated)
    {
        var console = new TestConsole();
        console.Profile.Width = Terminal.UnwrappedWidth;
        var processes = new FakeProcesses();

        new Launcher(console, _root, interactive: false, processes)
            .Run(["new", "-n", name, "--yes", "--template-source", Repository.Root])
            .ShouldBe(generated ? 0 : 1, console.Output);

        if (generated)
        {
            console.Output.ShouldContain($"dotnet new cleanarch-webapi -n {name} -o {name}");
            return;
        }

        console.Output.ShouldContain($"--name '{name}': -n takes two characters or more");
        console.Output.ShouldNotContain("name-needs-two-characters");
        Directory.Exists(Path.Combine(_root, name)).ShouldBeFalse();
        processes.Calls.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("_")]
    [InlineData("_Foo")]
    [InlineData("_1")]
    [InlineData("1a")]
    [InlineData("Acme._Shop")]
    [InlineData("Acme.1Shop")]
    [InlineData("Acme-Shop")]
    [InlineData("Acme..Shop")]
    [InlineData("Acme.")]
    [InlineData("Ab")]
    [InlineData("A_b")]
    [InlineData("Ab1.C_2")]
    [InlineData("Abc_")]
    [InlineData("Ab1.C2")]
    [InlineData("Northwind.Billing")]
    public void AProjectName_IsRefusedExactlyWhenItBreaksTheRuleItsMessageStates(string name)
    {
        const string rule = "use the letters A to Z, digits and dots, with each part starting with a letter, like Acme.Shop.";
        bool followsTheRule = name.Split('.').All(part => part.Length > 0 && char.IsAsciiLetter(part[0]) && part.All(char.IsAsciiLetterOrDigit));
        var console = new TestConsole();
        console.Profile.Width = Terminal.UnwrappedWidth;
        var processes = new FakeProcesses();

        new Launcher(console, _root, interactive: false, processes)
            .Run(["new", "-n", name, "--yes", "--template-source", Repository.Root])
            .ShouldBe(followsTheRule ? 0 : 1, console.Output);

        if (followsTheRule)
        {
            console.Output.ShouldNotContain(rule);
            return;
        }

        console.Output.ShouldContain($"--name '{name}': {rule}");
        processes.Calls.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("Abc_Def")]
    [InlineData("Acme.Shop_")]
    [InlineData("Ab.C_d.Ef")]
    public void AProjectName_WithAnUnderscore_IsRefused_SinceCA1707RefusesItInEveryNamespaceTheNameReaches(string name)
    {
        var console = new TestConsole();
        console.Profile.Width = Terminal.UnwrappedWidth;
        var processes = new FakeProcesses();

        new Launcher(console, _root, interactive: false, processes)
            .Run(["new", "-n", name, "--yes", "--template-source", Repository.Root])
            .ShouldBe(1, console.Output);

        console.Output.ShouldContain($"--name '{name}': use the letters A to Z, digits and dots");
        processes.Calls.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("presentation", "w", "--project-name takes two characters or more")]
    [InlineData("web-app", "w", "--project-name takes two characters or more")]
    [InlineData("presentation", "_1", "--project-name starts with a letter from A to Z")]
    [InlineData("web-app", "__", "--project-name starts with a letter from A to Z")]
    [InlineData("presentation", "Wx", null)]
    [InlineData("web-app", "Wx", null)]
    public void AnItemCommand_InAProjectWhoseNameTheItemTemplateRefuses_IsRefusedBeforeAnythingRuns(string command, string project, string? rule)
    {
        File.WriteAllText(Path.Combine(_root, project + ".slnx"), string.Empty);
        Directory.CreateDirectory(Path.Combine(_root, "Tools"));
        File.WriteAllText(Path.Combine(_root, "Tools", "Tasks.cs"), string.Empty);
        var console = new TestConsole();
        console.Profile.Width = Terminal.UnwrappedWidth;
        var processes = new FakeProcesses();

        int exit = new Launcher(console, _root, interactive: false, processes)
            .Run([command, "-n", command == "presentation" ? "Portal" : "Site", "--framework", command == "presentation" ? "blazor-server" : "vite-react", "--yes", "--template-source", Repository.Root]);

        if (rule is null)
        {
            exit.ShouldBe(0, console.Output);
            processes.Calls.ShouldNotBeEmpty();
            return;
        }

        exit.ShouldBe(1);
        console.Output.ShouldContain($"refuses {project}, whatever the answers: {rule}");
        console.Output.ShouldContain("Nothing was written");
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

using CleanArch.Create.Commands;
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

    [Fact]
    public void WithoutQuestions_TheCommandPrinted_IsDotnetNews_WithTheHiveGiven()
    {
        var console = new TestConsole();
        console.Profile.Width = Terminal.UnwrappedWidth;

        new Launcher(console, _root, interactive: false, new FakeProcesses())
            .Run(["new", "-n", "Acme.Shop", "--yes", "--template-source", Repository.Root, "--hive", "hive"])
            .ShouldBe(0, console.Output);

        console.Output.ShouldNotContain(LauncherCommands.Invocation);
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
    [InlineData("Acme.Shop", "Acme.Shop")]
    [InlineData("my-shop", "MyShop")]
    [InlineData("acme.online_shop", "Acme.OnlineShop")]
    [InlineData("2024 tests", "MyCompany.MyProject")]
    public void WithoutAName_TheProjectIsNamedAfterItsFolder_AndGeneratedInIt_EvenWhenItHoldsGit(string folder, string name)
    {
        string current = Directory.CreateDirectory(Path.Combine(_root, folder)).FullName;
        Directory.CreateDirectory(Path.Combine(current, ".git"));
        var console = new TestConsole();
        console.Profile.Width = Terminal.UnwrappedWidth;
        var processes = new FakeProcesses();

        new Launcher(console, current, interactive: false, processes).Run(["new", "--yes", "--template-source", Repository.Root]).ShouldBe(0, console.Output);

        console.Output.ShouldContain($"dotnet new cleanarch-webapi -n {name} -o .");
        processes.CommandLines.ShouldContain(line => line.StartsWith($"dotnet new cleanarch-webapi -n {name} -o {current}", StringComparison.Ordinal));
    }

    [Fact]
    public void AFolderWithFiles_IsRefusedWithoutQuestions_BeforeAnythingIsInstalled()
    {
        File.WriteAllText(Path.Combine(_root, "kept.txt"), string.Empty);
        var console = new TestConsole();
        console.Profile.Width = Terminal.UnwrappedWidth;
        var processes = new FakeProcesses();

        new Launcher(console, _root, interactive: false, processes).Run(["new", "--yes", "--template-source", Repository.Root]).ShouldBe(1);

        console.Output.ShouldContain($"'{_root}' already has files in it. Pick another folder with --output, or remove them, .git excepted, with --overwrite.");
        processes.Calls.ShouldBeEmpty();
        File.Exists(Path.Combine(_root, "kept.txt")).ShouldBeTrue();
    }

    [Fact]
    public void Overwrite_EmptiesAFolderWithFilesButGit_ThenGeneratesInIt_WithoutQuestions()
    {
        Directory.CreateDirectory(Path.Combine(_root, ".git"));
        File.WriteAllText(Path.Combine(_root, ".git", "HEAD"), "ref: refs/heads/main");
        File.WriteAllText(Path.Combine(_root, "notes.txt"), string.Empty);
        var console = new TestConsole();
        console.Profile.Width = Terminal.UnwrappedWidth;
        var processes = new FakeProcesses();

        new Launcher(console, _root, interactive: false, processes).Run(["new", "--yes", "--overwrite", "--template-source", Repository.Root]).ShouldBe(0, console.Output);

        File.Exists(Path.Combine(_root, ".git", "HEAD")).ShouldBeTrue();
        File.Exists(Path.Combine(_root, "notes.txt")).ShouldBeFalse();
        processes.CommandLines.ShouldContain(line => line.StartsWith("dotnet new cleanarch-webapi -n ", StringComparison.Ordinal) && line.Contains($" -o {_root}", StringComparison.Ordinal));
    }
}

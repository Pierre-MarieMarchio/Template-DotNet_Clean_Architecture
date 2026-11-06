using System.Text.Json.Nodes;
using Shouldly;
using Xunit;

namespace CleanArch.Cli.Tests;

[Collection(ProcessWideState.Name)]
public sealed class TestCommandTests : IDisposable
{
    private readonly StringWriter _output = new();
    private readonly StringWriter _error = new();
    private readonly FakeDotnet _dotnet = new();

    [Fact]
    public void Test_PrintsAndRunsTheTestsOfTheSolutionAtTheRoot()
    {
        using var project = SampleSolution.WithSolution();
        SampleSolution.AddUnitAndIntegrationTests(project);

        Run(["test"], project.Root).ShouldBe(CleanArchCommand.Succeeded);

        _output.ToString().ShouldContain("dotnet test --solution Acme.Shop.sln --configuration Debug");
        _dotnet.Calls.ShouldBe(["test --solution Acme.Shop.sln --configuration Debug"]);
        _error.ToString().ShouldBeEmpty();
    }

    [Fact]
    public void Test_NoIntegration_BuildsEveryTestProjectAndRunsOnlyTheDockerFreeOnes()
    {
        using var project = SampleSolution.WithSolution();
        SampleSolution.AddUnitAndIntegrationTests(project);

        Run(["test", "--no-integration"], project.Root).ShouldBe(CleanArchCommand.Succeeded);

        _output.ToString().ShouldContain("Built, not run Tests/Integration: it needs Docker.");
        _output.ToString().ShouldContain("dotnet build artifacts/test/docker-free-build.slnf --configuration Debug");
        _dotnet.Calls.ShouldBe(
        [
            "build artifacts/test/docker-free-build.slnf --configuration Debug",
            "test --solution artifacts/test/docker-free.slnf --configuration Debug --no-build",
        ]);
        _dotnet.FilterContentsOf("artifacts/test/docker-free.slnf").ShouldContain("Tests/Unit/A.Unit.csproj");
        _dotnet.FilterContentsOf("artifacts/test/docker-free.slnf").ShouldNotContain("Tests/Integration/A.Integration.csproj");
        _dotnet.FilterContentsOf("artifacts/test/docker-free-build.slnf").ShouldContain("Tests/Integration/A.Integration.csproj");
        File.Exists(Path.Combine(project.Root, "artifacts", "test", "docker-free.slnf")).ShouldBeFalse();
        File.Exists(Path.Combine(project.Root, "artifacts", "test", "docker-free-build.slnf")).ShouldBeFalse();
        _error.ToString().ShouldBeEmpty();
    }

    [Fact]
    public void Test_NoIntegration_WhenEveryTestIsDockerFree_RunsThemAllWithoutBuildingFirst()
    {
        using var project = SampleSolution.WithSolution();
        SampleSolution.AddTestProject(project, "Tests/Unit", "A.Unit.csproj", SampleSolution.UnitProject);

        Run(["test", "--no-integration"], project.Root).ShouldBe(CleanArchCommand.Succeeded);

        _dotnet.Calls.ShouldBe(["test --solution artifacts/test/docker-free.slnf --configuration Debug"]);
        _error.ToString().ShouldBeEmpty();
    }

    [Fact]
    public void Test_SkipsTheTestsThatTargetWindowsAlone()
    {
        using var project = SampleSolution.WithSolution();
        SampleSolution.AddTestProject(project, "Tests/Unit", "A.Unit.csproj", SampleSolution.UnitProject);
        SampleSolution.AddTestProject(project, "Tests/Windows", "A.Windows.csproj", SampleSolution.WindowsProject);

        Run(["test"], project.Root).ShouldBe(CleanArchCommand.Succeeded);

        _output.ToString().ShouldContain("Skipped Tests/Windows: it targets net10.0-windows and runs on Windows alone.");
        _dotnet.Calls.ShouldBe(
        [
            "build artifacts/test/runnable.slnf --configuration Debug",
            "test --solution artifacts/test/runnable.slnf --configuration Debug --no-build",
        ]);
        _dotnet.FilterContentsOf("artifacts/test/runnable.slnf").ShouldNotContain("A.Windows.csproj");
        _error.ToString().ShouldBeEmpty();
    }

    [Fact]
    public void Test_ForwardsTheExitCodeOfTheTests()
    {
        using var project = SampleSolution.WithSolution();
        SampleSolution.AddUnitAndIntegrationTests(project);
        FakeDotnet.ExitWith(3);

        Run(["test"], project.Root).ShouldBe(3);
    }

    [Fact]
    public void Test_NoIntegration_StopsWhenTheBuildFails()
    {
        using var project = SampleSolution.WithSolution();
        SampleSolution.AddUnitAndIntegrationTests(project);
        FakeDotnet.ExitWith(2);

        Run(["test", "--no-integration"], project.Root).ShouldBe(2);

        _dotnet.Calls.ShouldBe(["build artifacts/test/docker-free-build.slnf --configuration Debug"]);
    }

    [Fact]
    public void Test_WithoutATestProject_FailsSayingARunOverNothingIsAFalseGreen()
    {
        using var project = SampleSolution.WithSolution();

        Run(["test"], project.Root).ShouldBe(CleanArchCommand.Failed);

        _error.ToString().ShouldContain("no test project under Tests/");
        _dotnet.Calls.ShouldBeEmpty();
    }

    [Fact]
    public void Test_RefusesOutsideAProject()
    {
        string folder = Directory.CreateTempSubdirectory("cleanarch-cli-").FullName;
        try
        {
            Run(["test"], folder).ShouldBe(CleanArchCommand.Failed);
        }
        finally
        {
            Directory.Delete(folder);
        }

        _error.ToString().ShouldStartWith("Not inside a CleanArch project");
        _error.ToString().ShouldContain(ProjectLocator.FileName);
        _output.ToString().ShouldBeEmpty();
        _dotnet.Calls.ShouldBeEmpty();
    }

    [Fact]
    public void Test_RefusesAProjectFileItCannotRead()
    {
        using var project = new SampleProject("{ not json");

        Run(["test"], project.Root).ShouldBe(CleanArchCommand.Failed);

        _error.ToString().ShouldContain(ProjectLocator.FileName);
        _output.ToString().ShouldBeEmpty();
        _dotnet.Calls.ShouldBeEmpty();
    }

    [Fact]
    public void HelpJson_ListsTestWithItsNoIntegrationOption()
    {
        using var project = SampleSolution.WithSolution();

        Run(["help", "--json"], project.Root).ShouldBe(CleanArchCommand.Succeeded);

        JsonArray commands = CommandsOf();
        JsonObject test = commands.Single(command => Text(command!["name"]) == "test").ShouldNotBeNull().AsObject();
        test["options"].ShouldNotBeNull().AsArray().Select(option => Text(option!["name"])).ShouldBe(["--no-integration"]);
        _error.ToString().ShouldBeEmpty();
    }

    public void Dispose()
    {
        _output.Dispose();
        _error.Dispose();
        _dotnet.Dispose();
    }

    private int Run(string[] arguments, string workingDirectory) =>
        CleanArchCommand.Run(arguments, workingDirectory, _output, _error);

    private JsonArray CommandsOf()
    {
        JsonObject document = JsonNode.Parse(_output.ToString()).ShouldNotBeNull().AsObject();
        _output.GetStringBuilder().Clear();
        return document["commands"].ShouldNotBeNull().AsArray();
    }

    private static string Text(JsonNode? node) => node.ShouldNotBeNull().GetValue<string>();
}

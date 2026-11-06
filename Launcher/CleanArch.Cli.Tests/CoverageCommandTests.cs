using Shouldly;
using Xunit;

namespace CleanArch.Cli.Tests;

[Collection(ProcessWideState.Name)]
public sealed class CoverageCommandTests : IDisposable
{
    private readonly StringWriter _output = new();
    private readonly StringWriter _error = new();
    private readonly FakeDotnet _dotnet = new();

    private const string _coveredRun =
        "test --solution Acme.Shop.slnx --configuration Debug --results-directory TestResults --coverage"
        + " --coverage-settings coverage.runsettings --coverage-output-format cobertura";

    [Fact]
    public void Coverage_RunsTheCoveredTests_ThenTheFloorCheck()
    {
        using var project = SampleSolution.WithSolution();
        SampleSolution.AddUnitAndIntegrationTests(project);
        SampleSolution.WithCoverageFloor(project);

        Run(["coverage"], project.Root).ShouldBe(CleanArchCommand.Succeeded);

        _dotnet.Calls.ShouldBe([_coveredRun, "run Tools/CoverageGate.cs --root TestResults --minimum 80"]);
        _error.ToString().ShouldBeEmpty();
    }

    [Fact]
    public void Coverage_RemovesTheResultsOfAPreviousRun()
    {
        using var project = SampleSolution.WithSolution();
        SampleSolution.AddUnitAndIntegrationTests(project);
        SampleSolution.WithCoverageFloor(project);
        string stale = Path.Combine(project.Folder("TestResults"), "stale.cobertura.xml");
        File.WriteAllText(stale, "<coverage />");

        Run(["coverage"], project.Root).ShouldBe(CleanArchCommand.Succeeded);

        File.Exists(stale).ShouldBeFalse();
    }

    [Fact]
    public void Coverage_ForwardsTheExitCodeOfTheTestsAndSkipsTheFloorCheck()
    {
        using var project = SampleSolution.WithSolution();
        SampleSolution.AddUnitAndIntegrationTests(project);
        SampleSolution.WithCoverageFloor(project);
        _dotnet.ExitWith(5);

        Run(["coverage"], project.Root).ShouldBe(5);

        _dotnet.Calls.ShouldBe([_coveredRun]);
    }

    [Fact]
    public void Coverage_ForwardsTheExitCodeOfTheFloorCheck()
    {
        using var project = SampleSolution.WithSolution();
        SampleSolution.AddUnitAndIntegrationTests(project);
        SampleSolution.WithCoverageFloor(project, floor: "89.5");
        _dotnet.ExitWith(0);

        Run(["coverage"], project.Root).ShouldBe(CleanArchCommand.Succeeded);
        _dotnet.Calls.ShouldBe([_coveredRun, "run Tools/CoverageGate.cs --root TestResults --minimum 89.5"]);
    }

    [Fact]
    public void Coverage_WithoutAFloor_FailsSayingTheFileStatesNone()
    {
        using var project = SampleSolution.WithSolution();
        SampleSolution.AddUnitAndIntegrationTests(project);
        File.WriteAllText(Path.Combine(project.Root, "coverage.minimum"), "# no floor here\n");

        Run(["coverage"], project.Root).ShouldBe(CleanArchCommand.Failed);

        _error.ToString().ShouldContain("coverage.minimum");
        _error.ToString().ShouldContain("no coverage floor");
        _dotnet.Calls.ShouldBe([_coveredRun]);
    }

    public void Dispose()
    {
        _output.Dispose();
        _error.Dispose();
        _dotnet.Dispose();
    }

    private int Run(string[] arguments, string workingDirectory) =>
        BuiltInCommands.Run(arguments, workingDirectory, _output, _error);
}

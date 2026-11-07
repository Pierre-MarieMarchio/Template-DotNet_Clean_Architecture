using Shouldly;
using Xunit;

namespace CleanArch.Cli.Tests;

[Collection(ProcessWideState.Name)]
public sealed class TestCommandTests : IDisposable
{
    private const string _stepSummary = "GITHUB_STEP_SUMMARY";

    private readonly StringWriter _output = new();
    private readonly StringWriter _error = new();
    private readonly FakeDotnet _dotnet = new();
    private readonly string? _savedStepSummary = Environment.GetEnvironmentVariable(_stepSummary);

    public TestCommandTests() => Environment.SetEnvironmentVariable(_stepSummary, null);

    [Fact]
    public void Test_WithReportsOnly_TotalsTheTrxReportsAlreadyWritten_AppendsThemToTheSummary_AndRunsNothing()
    {
        using var project = SampleSolution.WithSolution();
        Trx(project, "one/a.trx", total: 10, passed: 7, failed: 2, notExecuted: 1);
        Trx(project, "two/b.trx", total: 5, passed: 5, failed: 0, notExecuted: 0);
        File.WriteAllText(Path.Combine(project.Folder("TestResults/two"), "unfinished.trx"), "<TestRun />");
        File.WriteAllText(Path.Combine(project.Root, "summary.md"), "Before.\n");

        Run(["test", "--reports-only", "--summary", "summary.md"], project.Root).ShouldBe(CleanArchCommand.Succeeded, _error.ToString());

        _output.ToString().ShouldContain("Read 3 TRX file(s) under TestResults.");
        File.ReadAllText(Path.Combine(project.Root, "summary.md"))
            .ShouldBe("Before.\n### Test results\n\n| Total | Passed | Failed | Skipped |\n|---:|---:|---:|---:|\n| 15 | 12 | 2 | 1 |\n");
        _dotnet.Calls.ShouldBeEmpty();
    }

    [Fact]
    public void Test_WithASummary_ReportsEachModule_ForgetsEarlierReports_AndFailsARunThatExecutedNoTest()
    {
        using var project = SampleSolution.WithSolution();
        SampleSolution.AddTestProject(project, "Tests/Unit", "A.Unit.csproj", SampleSolution.UnitProject);
        Trx(project, "earlier/a.trx", total: 4, passed: 4, failed: 0, notExecuted: 0);

        Run(["test", "--summary", "summary.md"], project.Root).ShouldBe(CleanArchCommand.Failed);

        _dotnet.Calls.ShouldBe(["test --solution Acme.Shop.slnx --configuration Debug --results-directory TestResults --report-xunit-trx"]);
        _error.ToString().ShouldContain("The run executed no test.");
        File.ReadAllText(Path.Combine(project.Root, "summary.md")).ShouldContain("| 0 | 0 | 0 | 0 |");
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
    public void Test_CappedForAMachineShortOfMemory_BuildsWithTheNodes_ThenTestsWithTheModules()
    {
        using var project = SampleSolution.WithSolution();
        SampleSolution.AddTestProject(project, "Tests/Unit", "A.Unit.csproj", SampleSolution.UnitProject);

        Run(["test", "-m:0"], project.Root).ShouldBe(CleanArchCommand.Failed);
        _dotnet.Calls.ShouldBeEmpty();

        Run(["test", "-m:2", "--configuration", "Release"], project.Root).ShouldBe(CleanArchCommand.Succeeded);

        _dotnet.Calls.ShouldBe(
        [
            "build Acme.Shop.slnx --configuration Release -maxcpucount:2",
            "test --solution Acme.Shop.slnx --configuration Release --no-build --max-parallel-test-modules 2",
        ]);
    }

    [Fact]
    public void Test_NoIntegration_StopsWhenTheBuildFails()
    {
        using var project = SampleSolution.WithSolution();
        SampleSolution.AddUnitAndIntegrationTests(project);
        _dotnet.ExitWith(2);

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

    public void Dispose()
    {
        Environment.SetEnvironmentVariable(_stepSummary, _savedStepSummary);
        _output.Dispose();
        _error.Dispose();
    }

    private static void Trx(SampleProject project, string relative, int total, int passed, int failed, int notExecuted)
    {
        string path = Path.Combine(project.Root, "TestResults", relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(
            path,
            $"""
            <?xml version="1.0" encoding="utf-8"?>
            <TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">
              <ResultSummary outcome="Completed">
                <Counters total="{total}" executed="{total - notExecuted}" passed="{passed}" failed="{failed}" notExecuted="{notExecuted}" />
              </ResultSummary>
            </TestRun>
            """);
    }

    private int Run(string[] arguments, string workingDirectory) =>
        BuiltInCommands.Run(arguments, workingDirectory, _output, _error, _dotnet);
}

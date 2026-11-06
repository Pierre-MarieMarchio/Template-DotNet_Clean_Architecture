using Shouldly;
using Xunit;

namespace CleanArch.Cli.Tests;

[Collection(ProcessWideState.Name)]
public sealed class CoverageCommandTests : IDisposable
{
    private const string _stepSummary = "GITHUB_STEP_SUMMARY";

    private const string _coveredRun =
        "test --solution Acme.Shop.slnx --configuration Debug --results-directory TestResults --coverage"
        + " --coverage-settings coverage.runsettings --coverage-output-format cobertura";

    private const string _ghost = "<package name=\"App.Ghost\"><classes></classes></package>";

    private readonly StringWriter _output = new();
    private readonly StringWriter _error = new();
    private readonly FakeDotnet _dotnet = new();
    private readonly string? _savedStepSummary = Environment.GetEnvironmentVariable(_stepSummary);

    public CoverageCommandTests() => Environment.SetEnvironmentVariable(_stepSummary, null);

    public static TheoryData<string, string[], double, int> Verdicts => new()
    {
        { "no report at all", [], 85, 2 },
        { "a valid report holding no package", [Report()], 85, 2 },
        { "a report whose only assembly has no measurable line", [Report(_ghost)], 85, 2 },
        { "XML that does not parse", ["<coverage><packages>"], 85, 2 },
        { "coverage genuinely below the floor", [Report(Package("App.Domain", "A.cs", (1, 1), (2, 0)))], 85, 1 },
        { "coverage above the floor", [Report(Package("App.Domain", "A.cs", (1, 1), (2, 1)))], 85, 0 },
        { "coverage exactly on the floor", [Report(Package("App.Domain", "A.cs", (1, 1), (2, 0)))], 50, 0 },
        {
            "two suites covering the same line, merged as a union rather than summed",
            [Report(Package("App.Domain", "A.cs", (1, 0), (2, 1))), Report(Package("App.Domain", "A.cs", (1, 1), (2, 0)))],
            100,
            0
        },
        {
            "an assembly with no measurable line standing beside one that has some",
            [Report(Package("App.Domain", "A.cs", (1, 1), (2, 1)), _ghost)],
            85,
            0
        },
    };

    [Fact]
    public void Coverage_RunsTheCoveredTests_ThenJudgesTheReportsTheyWrote()
    {
        using var project = SampleSolution.WithSolution();
        SampleSolution.AddUnitAndIntegrationTests(project);
        SampleSolution.WithCoverageFloor(project);
        var processes = new ReportWriter(_dotnet, project, Report(Package("App.Domain", "A.cs", (1, 1), (2, 1))));

        Run(["coverage"], project.Root, processes).ShouldBe(CleanArchCommand.Succeeded);

        _dotnet.Calls.ShouldBe([_coveredRun]);
        _output.ToString().ShouldContain("Coverage 100.00% >= floor 80%.");
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

        Run(["coverage"], project.Root).ShouldBe(2);

        File.Exists(stale).ShouldBeFalse();
    }

    [Fact]
    public void Coverage_ForwardsTheExitCodeOfTheTestsAndSkipsTheFloorCheck()
    {
        using var project = SampleSolution.WithSolution();
        SampleSolution.AddUnitAndIntegrationTests(project);
        SampleSolution.WithCoverageFloor(project);
        _dotnet.ExitWith(5);

        Run(["coverage"], project.Root, new ReportWriter(_dotnet, project, Report(Package("App.Domain", "A.cs", (1, 1))))).ShouldBe(5);

        _output.ToString().ShouldNotContain("floor");
    }

    [Fact]
    public void Coverage_ReadsTheFloorFromTheFirstBareNumber()
    {
        using var project = SampleSolution.WithSolution();
        SampleSolution.AddUnitAndIntegrationTests(project);
        SampleSolution.WithCoverageFloor(project, floor: "89.5");
        var processes = new ReportWriter(_dotnet, project, Report(Package("App.Domain", "A.cs", (1, 1), (2, 1))));

        Run(["coverage"], project.Root, processes).ShouldBe(CleanArchCommand.Succeeded);

        _output.ToString().ShouldContain("floor 89.5%");
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

    [Theory]
    [MemberData(nameof(Verdicts))]
    public void ReportsOnly_JudgesTheReportsAgainstTheFloorWithoutRunningTests(string label, string[] reports, double floor, int expected)
    {
        using var project = SampleSolution.WithSolution();
        SampleSolution.WithCoverageFloor(project, floor.ToString(System.Globalization.CultureInfo.InvariantCulture));
        WriteReports(project, reports);

        Run(["coverage", "--reports-only"], project.Root).ShouldBe(expected, label);

        _dotnet.Calls.ShouldBeEmpty();
    }

    [Fact]
    public void ReportsOnly_MergesTwoReportsOfTheSameFileAsAUnionOfLines()
    {
        using var project = SampleSolution.WithSolution();
        SampleSolution.WithCoverageFloor(project);
        WriteReports(
            project,
            [Report(Package("App.Domain", "A.cs", (1, 0), (2, 1))), Report(Package("App.Domain", "A.cs", (1, 1), (2, 0)))]);
        string json = Path.Combine(project.Root, "summary.json");

        Run(["coverage", "--reports-only", "--json", json], project.Root).ShouldBe(CleanArchCommand.Succeeded);

        string written = File.ReadAllText(json);
        written.ShouldContain("\"linesCovered\": 2,");
        written.ShouldContain("\"linesValid\": 2,");
        written.ShouldContain("\"branchRate\": 100.0,");
        written.ShouldContain("\"TestResults/0/coverage.cobertura.xml\"");
    }

    [Fact]
    public void ReportsOnly_AppendsTheTableToTheSummaryFileAndToTheGitHubStepSummary()
    {
        using var project = SampleSolution.WithSolution();
        SampleSolution.WithCoverageFloor(project);
        WriteReports(project, [Report(Package("App.Domain", "A.cs", (1, 1), (2, 1)))]);
        string summary = Path.Combine(project.Root, "summary.md");
        string step = Path.Combine(project.Root, "step.md");
        File.WriteAllText(summary, "kept\n");
        Environment.SetEnvironmentVariable(_stepSummary, step);

        Run(["coverage", "--reports-only", "--summary", summary], project.Root).ShouldBe(CleanArchCommand.Succeeded);

        File.ReadAllText(summary).ShouldStartWith("kept\n### Coverage");
        File.ReadAllText(summary).ShouldContain("| `App.Domain` | 2/2 | 100.00% |");
        File.ReadAllText(step).ShouldBe(File.ReadAllText(summary)["kept\n".Length..]);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable(_stepSummary, _savedStepSummary);
        _output.Dispose();
        _error.Dispose();
    }

    private static string Report(params string[] packages) =>
        "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<coverage><packages>\n" + string.Join("\n", packages) + "\n</packages></coverage>\n";

    private static string Package(string name, string filename, params (int Number, int Hits)[] lines) =>
        $"<package name=\"{name}\"><classes><class name=\"{name}.Type\" filename=\"{filename}\"><lines>"
        + string.Concat(lines.Select(line => $"<line number=\"{line.Number}\" hits=\"{line.Hits}\" />"))
        + "</lines></class></classes></package>";

    private static void WriteReports(SampleProject project, string[] reports)
    {
        for (int index = 0; index < reports.Length; index++)
        {
            string folder = Path.Combine(project.Folder("TestResults"), index.ToString(System.Globalization.CultureInfo.InvariantCulture));
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "coverage.cobertura.xml"), reports[index]);
        }
    }

    private int Run(string[] arguments, string workingDirectory, IProcessLauncher? processes = null) =>
        BuiltInCommands.Run(arguments, workingDirectory, _output, _error, processes ?? _dotnet);

    private sealed class ReportWriter(FakeDotnet inner, SampleProject project, string report) : IProcessLauncher
    {
        public int Launch(ExternalCall call, TextWriter output, TextWriter error)
        {
            int exit = inner.Launch(call, output, error);
            if (call.Arguments.Contains("--coverage"))
            {
                WriteReports(project, [report]);
            }

            return exit;
        }
    }
}

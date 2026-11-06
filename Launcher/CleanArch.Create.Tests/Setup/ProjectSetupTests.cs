using CleanArch.Create.Setup;
using CleanArch.Tooling.Failures;
using CleanArch.Tooling.Manifests;
using CleanArch.Tooling.Processes;
using Shouldly;
using Xunit;

namespace CleanArch.Create.Tests.Setup;

public sealed class ProjectSetupTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("cleanarch-create-").FullName;
    private readonly FakeProcesses _processes;
    private bool _insideAWorkTree;
    private bool _identity = true;
    private string _status = " A README.md";
    private ProcessResult _bootstrap = new(0, string.Empty);
    private ProcessResult _add = new(0, string.Empty);

    public ProjectSetupTests()
    {
        File.WriteAllText(Path.Combine(_root, ".env.example"), "API_HOST_PORT=8080");
        _processes = new FakeProcesses(Answer);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Theory]
    [InlineData("none", "api", "")]
    [InlineData("angular", "api", ", creates each project or app a scaffold.json describes, installs the web workspace, which needs Node.js and pnpm")]
    [InlineData("none", "avalonia", ", creates each project or app a scaffold.json describes, writes the .NET client")]
    [InlineData("none", "api|worker", ", creates each project or app a scaffold.json describes")]
    [InlineData("placeholder", "api", ", installs the web workspace, which needs Node.js and pnpm")]
    public void TheBootstrapStep_SaysWhatBootstrapWritesForTheShapeChosen(string web, string presentation, string writes)
    {
        TemplateManifest manifest = Repository.Source.Manifest(TemplateKind.Project);
        var answers = new Dictionary<string, string>(StringComparer.Ordinal) { ["web"] = web, ["presentation"] = presentation };

        BootstrapStep.DescriptionFor(symbol => Condition.IsTruthy(manifest.Resolve(symbol, answers))).ShouldBe(
            "Runs " + BootstrapStep.Command + ": formatting, OpenAPI document, tools and first migrations"
            + writes + ". Needs internet.");
    }

    [Fact]
    public void EverySymbolTheBootstrapStepReads_IsComputedByTheProjectTemplate()
    {
        TemplateManifest manifest = Repository.Source.Manifest(TemplateKind.Project);

        BootstrapStep.WritesWhen.Select(fact => fact.Symbol).ShouldAllBe(
            symbol => manifest.Computed.ContainsKey(symbol),
            "The bootstrap step reads a symbol the project template no longer computes, so it would never say what bootstrap writes.");
    }

    [Fact]
    public void EveryStep_RunsInOrder_AndCommitsTwiceWithTheUsersIdentity()
    {
        List<SetupOutcome> outcomes = RunAll();

        outcomes.ShouldAllBe(outcome => outcome.Status == SetupStatus.Done);
        _processes.CommandLines.Where(line => !line.StartsWith("git config", StringComparison.Ordinal)).ShouldBe(
        [
            "git rev-parse --is-inside-work-tree",
            "git init",
            "git add -A -- .",
            "git status --porcelain -- .",
            "git commit -q -m chore: generate from cleanarch-webapi 1.2.3 -- .",
            "dotnet run Tools/Tasks.cs bootstrap",
            "git rev-parse --is-inside-work-tree",
            "git add -A -- .",
            "git status --porcelain -- .",
            "git commit -q -m chore: bootstrap -- .",
        ]);
        _processes.Calls.ShouldAllBe(call => call.WorkingDirectory == _root);
        File.ReadAllText(Path.Combine(_root, ".env")).ShouldBe("API_HOST_PORT=8080");
    }

    [Fact]
    public void InsideAWorkTree_TheFolderIsNotInitialised()
    {
        _insideAWorkTree = true;

        RunAll();

        _processes.CommandLines.ShouldNotContain("git init");
    }

    [Fact]
    public void WithoutAGitIdentity_TheCommitsAreSkipped_WithTheConfigToRun_AndBootstrapStillRuns()
    {
        _identity = false;

        List<SetupOutcome> outcomes = RunAll();

        outcomes.Select(outcome => outcome.Status).ShouldBe([SetupStatus.Skipped, SetupStatus.Done, SetupStatus.Skipped, SetupStatus.Done]);
        outcomes[0].Rerun.ShouldContain("git config --global user.name \"<your name>\"");
        outcomes[0].Rerun.ShouldContain("git config --global user.email \"<your email>\"");
        outcomes[0].Rerun.ShouldContain("git commit -m \"chore: generate from cleanarch-webapi 1.2.3\"");
        _processes.CommandLines.ShouldNotContain(line => line.StartsWith("git commit", StringComparison.Ordinal));
        _processes.CommandLines.ShouldContain("dotnet run Tools/Tasks.cs bootstrap");
    }

    [Fact]
    public void AFailedBootstrap_SaysWhy_AndSkipsItsCommit_NotTheEnvironment()
    {
        _bootstrap = new ProcessResult(1, "Restoring tools\nerror NU1301: Unable to load the service index for source https://api.nuget.org/v3/index.json.\n");

        List<SetupOutcome> outcomes = RunAll();

        outcomes[1].Status.ShouldBe(SetupStatus.Failed);
        string why = outcomes[1].Why.ShouldNotBeNull();
        why.ShouldContain("exited with code 1");
        why.ShouldContain("NU1301");
        outcomes[1].Rerun.ShouldBe([BootstrapStep.Command]);
        outcomes[2].Status.ShouldBe(SetupStatus.Skipped);
        outcomes[2].Rerun.ShouldContain("git commit -m \"chore: bootstrap\"");
        outcomes[3].Status.ShouldBe(SetupStatus.Done);
        _processes.CommandLines.ShouldNotContain("git commit -q -m chore: bootstrap -- .");
    }

    [Fact]
    public void AFailedBootstrap_NamesTheLastError_TheJournalWithTheFullOutput_AndTheCommandToRun()
    {
        string output = "error CS0246: The type or namespace name 'Missing' could not be found\n" + string.Join('\n', Enumerable.Range(1, 8).Select(number => $"line {number}")) + "\n";
        _bootstrap = new ProcessResult(1, output);

        SetupOutcome outcome = RunAll()[1];

        string journal = Path.Combine(_root, "artifacts", "bootstrap.log");
        string why = outcome.Why.ShouldNotBeNull();
        why.ShouldContain("Last error: error CS0246");
        why.ShouldContain("line 8");
        why.ShouldContain("The full output is in " + journal);
        File.ReadAllText(journal).ShouldContain("line 1");
        File.ReadAllText(journal).ShouldContain("error CS0246");
        outcome.Rerun.ShouldBe([BootstrapStep.Command]);
    }

    [Fact]
    public void AProjectToolNoSourceHolds_IsNamedWithItsCause_AndTheInstallThatFixesIt()
    {
        _bootstrap = new ProcessResult(1, "Build succeeded.\n    0 Error(s)\n> dotnet tool install CleanArch.Cli --local --version 1.2.3\nVersion 1.2.3 of package cleanarch.cli is not found in NuGet feeds https://api.nuget.org/v3/index.json.\n'dotnet tool install CleanArch.Cli' exited with 1.\n");

        SetupOutcome outcome = RunAll()[1];

        outcome.Status.ShouldBe(SetupStatus.Failed);
        string why = outcome.Why.ShouldNotBeNull();
        why.ShouldContain("Last error: Version 1.2.3 of package cleanarch.cli is not found in NuGet feeds");
        why.ShouldContain("CleanArch.Cli 1.2.3, the tool this project pins, isn't on a NuGet source this machine uses");
        why.ShouldContain("CleanArch.Cli.1.2.3.nupkg");
        why.ShouldContain("then run bootstrap again");
        outcome.Rerun.ShouldBe(["dotnet tool install CleanArch.Cli --local --version 1.2.3 --add-source <folder holding CleanArch.Cli.1.2.3.nupkg>", BootstrapStep.Command]);
    }

    [Theory]
    [InlineData("error NU1102: Unable to find package CleanArch.Cli with version (>= 1.2.3)", true)]
    [InlineData("error NU1101: Unable to find package Some.Other.Tool", false)]
    [InlineData("error NU1301: Unable to load the service index for source https://api.nuget.org/v3/index.json.", false)]
    public void TheProjectToolNoSourceHolds_IsToldApartFromOtherRestoreFailures(string output, bool expected) =>
        BootstrapStep.ProjectToolNotFound(output).ShouldBe(expected);

    [Theory]
    [InlineData("GIT_AUTHOR_NAME", "GIT_COMMITTER_NAME", "GIT_AUTHOR_EMAIL", "GIT_COMMITTER_EMAIL", true)]
    [InlineData("GIT_AUTHOR_NAME", "", "GIT_AUTHOR_EMAIL", "", false)]
    public void TheGitEnvironmentVariables_CountAsAnIdentity_WhenAuthorAndCommitterAreBothThere(string authorName, string committerName, string authorEmail, string committerEmail, bool known)
    {
        _identity = false;
        string[] set = [.. new[] { authorName, committerName, authorEmail, committerEmail }.Where(name => name.Length > 0)];
        var setup = new ProjectSetup(_processes, _root, "1.2.3", null, name => set.Contains(name) ? "Ada" : null);

        SetupOutcome outcome = setup.Run(ProjectSetup.FirstCommit, []);

        (outcome.Status == SetupStatus.Done).ShouldBe(known);
        if (!known)
        {
            outcome.Why.ShouldNotBeNull().ShouldContain("GIT_COMMITTER_NAME");
        }
    }

    [Fact]
    public void AFailedFirstCommit_AfterASuccessfulInit_LeavesNoInit_AndItsCommitTakesWhatBootstrapWrote()
    {
        _add = new ProcessResult(128, "error: open(\"Src/Application/AS.Application.Auth/Features/Accounts/Model/Errors/AccountsErrors.cs\"): Filename too long\nfatal: adding files failed\n");

        List<SetupOutcome> outcomes = RunAll();

        outcomes.Select(outcome => outcome.Status).ShouldBe([SetupStatus.Failed, SetupStatus.Done, SetupStatus.Skipped, SetupStatus.Done]);
        outcomes[0].Why.ShouldNotBeNull().ShouldContain("Turn on long paths");
        outcomes[2].Rerun.ShouldBeEmpty();
        ProjectSetup.Left(outcomes).ShouldBe(
        [
            "git config --global core.longpaths true",
            "git add -A",
            "git commit -m \"chore: generate from cleanarch-webapi 1.2.3\"",
        ]);
    }

    [Fact]
    public void TheCommandsLeft_StageBeforeEachCommit_AndConfigureGitOnce()
    {
        _identity = false;
        _bootstrap = new ProcessResult(1, "error NU1301");

        List<string> left = [.. ProjectSetup.Left(RunAll())];

        left.Count(command => command == "git add -A").ShouldBe(2);
        left.Count(command => command == "git config --global user.name \"<your name>\"").ShouldBe(1);
        left.IndexOf(BootstrapStep.Command).ShouldBeLessThan(left.LastIndexOf("git add -A"));
    }

    [Fact]
    public void NothingToCommit_IsNoCommit()
    {
        _status = string.Empty;

        RunAll()[0].Status.ShouldBe(SetupStatus.Done);

        _processes.CommandLines.ShouldNotContain(line => line.StartsWith("git commit", StringComparison.Ordinal));
    }

    [Fact]
    public void AnEnvironmentFileThere_IsKept()
    {
        File.WriteAllText(Path.Combine(_root, ".env"), "KEPT=1");

        RunAll()[3].Status.ShouldBe(SetupStatus.Done);

        File.ReadAllText(Path.Combine(_root, ".env")).ShouldBe("KEPT=1");
    }

    [Fact]
    public void GitMissing_FailsTheCommits_WithTheirCommands()
    {
        var setup = new ProjectSetup(new FakeProcesses(call => call.FileName == "git" ? throw new LauncherException("git could not be started: it must be on the PATH.") : new ProcessResult(0, string.Empty)), _root, "1.2.3", null);

        SetupOutcome outcome = setup.Run(ProjectSetup.FirstCommit, []);

        outcome.Status.ShouldBe(SetupStatus.Failed);
        outcome.Why.ShouldBe("git could not be started: it must be on the PATH.");
        outcome.Rerun.ShouldContain("git add -A");
    }

    private List<SetupOutcome> RunAll()
    {
        var setup = new ProjectSetup(_processes, _root, "1.2.3", null);
        var outcomes = new List<SetupOutcome>();
        foreach (SetupStep step in ProjectSetup.Steps)
        {
            outcomes.Add(setup.Run(step, outcomes));
        }

        return outcomes;
    }

    private ProcessResult Answer(ProcessCall call) => (call.FileName, call.Arguments[0]) switch
    {
        ("git", "init") => Initialised(),
        ("git", "rev-parse") => _insideAWorkTree ? new ProcessResult(0, "true\n") : new ProcessResult(128, "fatal: not a git repository"),
        ("git", "config") => _identity ? new ProcessResult(0, "Ada Lovelace\n") : new ProcessResult(1, string.Empty),
        ("git", "status") => new ProcessResult(0, _status),
        ("git", "add") => _add,
        ("dotnet", "run") => _bootstrap,
        _ => new ProcessResult(0, string.Empty),
    };

    private ProcessResult Initialised()
    {
        _insideAWorkTree = true;
        return new ProcessResult(0, string.Empty);
    }
}

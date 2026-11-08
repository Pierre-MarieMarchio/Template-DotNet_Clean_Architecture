using System.Text.RegularExpressions;
using CleanArch.Create.Commands;
using CleanArch.Create.Hosting;
using CleanArch.Create.Setup;
using CleanArch.Tooling.Additions;
using CleanArch.Tooling.Processes;
using CleanArch.Tooling.Prompts;
using CleanArch.Tooling.Templates;
using Shouldly;
using Spectre.Console;
using Spectre.Console.Testing;
using Xunit;

namespace CleanArch.Create.Tests.Prompts;

public sealed partial class GuidedFlowTests : IDisposable
{
    private const string _created = "The template was created successfully.";
    private const string _bootstrapInstruction = "Manual instructions: run dotnet tool restore, then dotnet cleanarch bootstrap.";
    private const string _runIt =
        """
        ## Run it

        You need Docker.

        ```bash
        dotnet tool restore                     # once, after a fresh clone
        dotnet cleanarch bootstrap              # once, after a fresh clone; commit what it changes
        cp .env.example .env
        docker compose up -d --build --wait
        ```

        ## Everyday commands
        """;

    private const string _wgl4AboveLatin1 = "─│┌┐└┘├┤┬┴┼═║╒╓╔╕╖╗╘╙╚╛╜╝╞╟╠╡╢╣╤╥╦╧╨╩╪╫╬▀▄█▌▐░▒▓■□▪▫▬▲►▼◄◊○●◘◙◦♦←↑→↓–—‘’“”…";

    private readonly string _root = Directory.CreateTempSubdirectory("cleanarch-create-").FullName;
    private readonly FakeProcesses _processes;
    private bool _identity = true;
    private bool _initialised;
    private ProcessResult _bootstrap = new(0, string.Empty);
    private Action? _duringBootstrap;

    public GuidedFlowTests()
    {
        _processes = new FakeProcesses(Answer);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void ANewProject_IsABannerThenAThread_ASummary_TheSetUp_AndTheCommandsLeft()
    {
        TestConsole console = Terminal();
        console.Input.PushTextWithEnter("Demo");
        PushEnters(console, 12);

        Run(console, "new");

        string output = Plain(console);
        output.ShouldContain("██████╗");
        output.ShouldContain(Banner.Tool);
        output.ShouldContain(Banner.Tagline);
        output.ShouldContain("◊  Project name\n│  Demo\n");
        output.ShouldContain("◊  Database\n│  postgresql\n");
        output.ShouldContain("◊  Set up the project now?\n│  First commit, Bootstrap, Bootstrap commit, Copy .env\n");
        Order(
            output,
            "┌  Create a project",
            "◊  Presentation project",
            "◊  First web app",
            "◊  Authentication",
            "◊  Database",
            "♦  Set up the project now?",
            "Summary",
            "◊  Generated Demo",
            "◊  First commit done",
            "◊  Bootstrap done",
            "◊  Bootstrap commit done",
            "◊  Copy .env done",
            "Next time, skip the questions with",
            "--setup",
            "└  All set. Now run:");
        output.ShouldContain("┌──");
        output.ShouldMatch(@"│ Authentication\s+│ local\s+│");
        output.ShouldMatch(@"│ Output folder\s+│ ./Demo\s+│");
        output.ShouldMatch(@"│ Setup steps\s+│ First commit, Bootstrap, Bootstrap commit, Copy .env\s+│");
        string left = Closing(output);
        left.ShouldContain("     cd ./Demo");
        left.ShouldContain("     docker compose up -d --build --wait");
        left.ShouldNotContain(BootstrapStep.Restore);
        left.ShouldNotContain(BootstrapStep.Command);
        left.ShouldNotContain(EnvironmentFileStep.Command);
        left.ShouldNotContain("```");
        output.ShouldNotContain(_created);
        _processes.CommandLines.ShouldContain(line => line.StartsWith("dotnet new cleanarch-webapi -n Demo -o ", StringComparison.Ordinal));
        File.Exists(Path.Combine(_root, "Demo", ".env")).ShouldBeTrue();
    }

    [Fact]
    public void EveryGlyphDrawn_IsOneConsoleFontsCarry_SoTheThreadStaysAligned()
    {
        TestConsole console = Terminal();
        console.Input.PushTextWithEnter("Demo");
        PushEnters(console, 12);

        Run(console, "new");

        Plain(console).Where(glyph => glyph > 'ÿ' && !_wgl4AboveLatin1.Contains(glyph)).Distinct().ShouldBeEmpty(
            "Outside the WGL4 set, a terminal takes the glyph from a fallback font, which draws it off the rail.");
    }

    [Fact]
    public void AfterQuestions_TheLaunchersCommand_KeepsTheHiveGiven()
    {
        TestConsole console = Terminal();
        console.Input.PushTextWithEnter("Demo");
        PushEnters(console, 12);

        Run(console, "new", "--hive", "hive");

        string output = Plain(console);
        output.ShouldContain("dnx CleanArch.Create -- new -n Demo -o ./Demo --yes --setup");
        output.ShouldMatch(@"[^:]--hive\s+(│\s+)?hive");
        output.ShouldContain("--debug:custom-hive hive");
    }

    [Fact]
    public void AfterQuestions_TheLaunchersCommand_ReplaysThroughTheArgumentsAfterTheSeparator()
    {
        TestConsole console = Terminal();
        console.Profile.Width = 1000;
        console.Input.PushTextWithEnter("Demo");
        PushEnters(console, 12);

        Run(console, "new");

        string printed = Regex.Match(Plain(console), @"^.*(?<command>dnx CleanArch\.Create -- new .*--yes.*)$", RegexOptions.Multiline).Groups["command"].Value.Trim();
        Arguments replayed = Arguments.Parse(printed[(LauncherCommands.InvocationWithArguments.Length + 1)..].Split(' '));

        replayed.Name.ShouldBe("Demo");
        replayed.Yes.ShouldBeTrue();
    }

    [Theory]
    [InlineData("", "")]
    [InlineData("Demo", "Demo")]
    public void TheNameTheFolderProposes_GeneratesInTheFolder_AndANameTyped_InASubfolderOfThatName(string typed, string subfolder)
    {
        string folder = Directory.CreateDirectory(Path.Combine(_root, "my-shop")).FullName;
        TestConsole console = Terminal();
        console.Input.PushTextWithEnter(typed);
        PushEnters(console, 12);

        new Launcher(console, folder, interactive: true, _processes).Run(["new", "--template-source", Repository.Root]).ShouldBe(0, Plain(console));

        string output = Plain(console);
        output.ShouldContain("◊  Project name\n│  " + (typed.Length == 0 ? "MyShop" : typed) + "\n");
        _processes.Calls.ShouldContain(call => call.Arguments.Contains("-o") && call.Arguments[call.Arguments.ToList().IndexOf("-o") + 1] == Path.Combine(folder, subfolder).TrimEnd(Path.DirectorySeparatorChar));
        Closing(output).Contains("cd ", StringComparison.Ordinal).ShouldBe(subfolder.Length > 0);
        _processes.Calls.Single(call => call.Arguments.Contains("cleanarch-webapi")).Arguments.ShouldNotContain("--force");
    }

    [Fact]
    public void RemovingTheFilesAlreadyInTheFolder_KeepsGit_AndHappensBeforeTheGeneration()
    {
        string folder = AFolderWithFiles();
        TestConsole console = Terminal();
        PushEnters(console, 1);
        console.Input.PushKey(ConsoleKey.DownArrow);
        PushEnters(console, 13);

        new Launcher(console, folder, interactive: true, _processes).Run(["new", "--template-source", Repository.Root]).ShouldBe(0, Plain(console));

        Order(Plain(console), "already has files in it\n│  Remove existing files and continue", "◊  Removed the files in", "◊  Generated MyShop");
        _processes.Calls.Single(call => call.Arguments.Contains("cleanarch-webapi")).Arguments.ShouldNotContain("--force");
        File.Exists(Path.Combine(folder, ".git", "HEAD")).ShouldBeTrue();
        File.Exists(Path.Combine(folder, "notes.txt")).ShouldBeFalse();
        Directory.Exists(Path.Combine(folder, "drafts")).ShouldBeFalse();
    }

    [Fact]
    public void CancellingOnAFolderWithFiles_TouchesNothing_AndRunsNothing()
    {
        string folder = AFolderWithFiles();
        TestConsole console = Terminal();
        PushEnters(console, 2);

        new Launcher(console, folder, interactive: true, _processes).Run(["new", "--template-source", Repository.Root]).ShouldBe(1, Plain(console));

        Plain(console).ShouldContain(ItemAdditions.Stopped);
        Directory.EnumerateFileSystemEntries(folder).Count().ShouldBe(3);
        _processes.Calls.ShouldBeEmpty();
    }

    [Fact]
    public void IgnoringTheFilesInTheFolder_GeneratesOverThem_KeepingThoseTheTemplateDoesNotWrite()
    {
        string folder = AFolderWithFiles();
        File.WriteAllText(Path.Combine(folder, "README.md"), "mine");
        TestConsole console = Terminal();
        PushEnters(console, 1);
        console.Input.PushKey(ConsoleKey.DownArrow);
        console.Input.PushKey(ConsoleKey.DownArrow);
        PushEnters(console, 13);

        new Launcher(console, folder, interactive: true, _processes).Run(["new", "--template-source", Repository.Root]).ShouldBe(0, Plain(console));

        _processes.Calls.Single(call => call.Arguments.Contains("cleanarch-webapi")).Arguments.ShouldContain("--force");
        File.ReadAllText(Path.Combine(folder, "README.md")).ShouldBe(_runIt);
        File.Exists(Path.Combine(folder, "notes.txt")).ShouldBeTrue();
    }

    [Fact]
    public void ARefusedAnswer_IsAnsweredOnTheThread_WithTheRefusalThatLeavesIt()
    {
        TestConsole console = Terminal();
        console.Input.PushTextWithEnter("Demo");
        PushEnters(console, 2);
        console.Input.PushKey(ConsoleKey.End);
        PushEnters(console, 8);

        Run(console, "new");

        string output = Plain(console);
        output.ShouldContain("◊  Example features\n│  none · the only option when Authentication is none\n");
        output.ShouldContain("◊  Files module\n│  No · the only option when Authentication is none\n");
        output.ShouldMatch(@"│ Example features\s+│ none\s+│");
    }

    [Fact]
    public void ASummaryRefused_GoesBackToTheLastQuestion_WithItsAnswerKept()
    {
        TestConsole console = Terminal();
        console.Input.PushTextWithEnter("Demo");
        PushEnters(console, 2);
        console.Input.PushKey(ConsoleKey.DownArrow);
        PushEnters(console, 8);
        console.Input.PushKey(ConsoleKey.DownArrow);
        console.Input.PushKey(ConsoleKey.Spacebar);
        console.Input.PushKey(ConsoleKey.Enter);
        console.Input.PushKey(ConsoleKey.RightArrow);
        PushEnters(console, 3);

        Run(console, "new");

        string output = Plain(console);
        Regex.Count(output, "Summary").ShouldBe(2);
        Regex.Count(output, "◊  Set up the project now\\?\n│  First commit, Bootstrap commit, Copy .env").ShouldBe(2);
        Regex.Count(output, "◊  Authentication").ShouldBe(1);
        output.ShouldNotContain("◊  Bootstrap done");
        _processes.CommandLines.ShouldContain(line => line.StartsWith("dotnet new cleanarch-webapi -n Demo -o ", StringComparison.Ordinal) && line.Contains("--auth external", StringComparison.Ordinal));
        _processes.CommandLines.ShouldNotContain(BootstrapStep.Command);
        Order(Closing(output), BootstrapStep.Restore, BootstrapStep.Command);
    }

    [Fact]
    public void GoingBack_ReopensTheLastQuestionAsked_AndWhatFollowsIsOfferedAnew()
    {
        TestConsole console = Terminal();
        console.Input.PushKey(ConsoleKey.Escape);
        console.Input.PushTextWithEnter("Demo");
        PushEnters(console, 2);
        console.Input.PushKey(ConsoleKey.End);
        PushEnters(console, 2);
        console.Input.PushKey(ConsoleKey.Escape);
        console.Input.PushKey(ConsoleKey.Escape);
        console.Input.PushKey(ConsoleKey.Home);
        PushEnters(console, 10);

        Run(console, "new");

        string output = Plain(console);
        Order(output, "◊  Authentication\n│  none", "◊  Example features\n│  none ·", "♦  GitHub Actions workflows", "♦  Database", "◊  Authentication\n│  local", "○ notes", "◊  Example features\n│  all");
        output.ShouldMatch(@"│ Authentication\s+│ local\s+│");
        output.ShouldMatch(@"│ Example features\s+│ all\s+│");
        _processes.CommandLines.ShouldContain(line => line.StartsWith("dotnet new cleanarch-webapi -n Demo -o ", StringComparison.Ordinal) && !line.Contains("--auth", StringComparison.Ordinal));
    }

    [Fact]
    public void AFailedBootstrap_IsReported_ItsCommitSkipped_AndBothLeftToRun()
    {
        _bootstrap = new ProcessResult(1, "error NU1301: Unable to load the service index for source https://api.nuget.org/v3/index.json.");
        TestConsole console = Terminal();
        console.Input.PushTextWithEnter("Demo");
        PushEnters(console, 12);

        Run(console, "new");

        string output = Plain(console);
        Order(output, "◊  First commit done", "▲  Bootstrap failed", "exited with code 1", "NU1301", "▲  Bootstrap commit skipped", "◊  Copy .env done", "└  Almost there. Now run:");
        string left = Closing(output);
        Order(left, "cd ./Demo", BootstrapStep.Command, "git add -A", "git commit -m \"chore: bootstrap\"", "docker compose up -d --build --wait");
        left.ShouldNotContain(EnvironmentFileStep.Command);
    }

    [Fact]
    public void AnInterruptDuringTheSetUp_SkipsTheStepsLeft_AndClosesOnWhatIsLeftToRun()
    {
        TestConsole console = Terminal();
        console.Input.PushTextWithEnter("Demo");
        PushEnters(console, 12);
        var launcher = new Launcher(console, _root, interactive: true, _processes);
        _duringBootstrap = () => launcher.Interrupt().ShouldBeTrue();

        launcher.Run(["new", "--template-source", Repository.Root]).ShouldBe(Launcher.StoppedExitCode);

        string output = Plain(console);
        Order(output, "◊  First commit done", "◊  Bootstrap done", "▲  Bootstrap commit skipped", "Skipped because the setup was stopped.", "▲  Copy .env skipped", "└  Almost there. Now run:");
        string left = Closing(output);
        Order(left, "cd ./Demo", "git add -A", "git commit -m \"chore: bootstrap\"", EnvironmentFileStep.Command);
        left.ShouldNotContain("git init");
        _processes.CommandLines.ShouldNotContain(line => line.StartsWith("git commit -q -m chore: bootstrap", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AnInterruptedSetUpWithoutTheFirstCommit_LeavesGitInitToRun_OnlyOutsideARepository(bool insideARepository)
    {
        _initialised = insideARepository;
        TestConsole console = Terminal();
        console.Input.PushTextWithEnter("Demo");
        PushEnters(console, 10);
        console.Input.PushKey(ConsoleKey.Spacebar);
        PushEnters(console, 2);
        var launcher = new Launcher(console, _root, interactive: true, _processes);
        _duringBootstrap = () => launcher.Interrupt();

        launcher.Run(["new", "--template-source", Repository.Root]).ShouldBe(Launcher.StoppedExitCode);

        string left = Closing(Plain(console));
        Order(left, "git commit -m \"chore: bootstrap\"", EnvironmentFileStep.Command);
        left.Contains("git init", StringComparison.Ordinal).ShouldBe(!insideARepository);
    }

    [Fact]
    public void AnInterruptOutsideTheSetUp_IsLeftToEndTheProcess()
    {
        new Launcher(new TestConsole(), _root, interactive: false, _processes).Interrupt().ShouldBeFalse();
    }

    [Fact]
    public void WithoutAGitIdentity_TheCommitsAreSkipped_WithTheConfigToRun()
    {
        _identity = false;
        TestConsole console = Terminal();
        console.Input.PushTextWithEnter("Demo");
        PushEnters(console, 12);

        Run(console, "new");

        string output = Plain(console);
        Order(output, "▲  First commit skipped", "git doesn't know who you are yet: user.name and user.email aren't set.", "◊  Bootstrap done", "▲  Bootstrap commit skipped");
        Closing(output).ShouldContain("git config --global user.email \"<your email>\"");
        _processes.CommandLines.ShouldNotContain(line => line.StartsWith("git commit", StringComparison.Ordinal));
    }

    [Fact]
    public void WithYes_TheApi_IsAddedForTheMcpServerUnderLocalAccounts_AndPassed()
    {
        TestConsole console = Terminal();

        Run(console, "new", "-n", "Demo", "--presentation", "mcp", "--yes");

        Plain(console).ShouldContain("Presentation projects: api, mcp (the API added for you: the MCP server under local accounts calls it)");
        _processes.CommandLines.ShouldContain(line => line.StartsWith("dotnet new cleanarch-webapi -n Demo -o ", StringComparison.Ordinal) && line.EndsWith("--presentation api --presentation mcp", StringComparison.Ordinal));
    }

    [Fact]
    public void WithYes_TheWorker_IsAddedForAPortalWithoutTheApi_AndPassed()
    {
        TestConsole console = Terminal();

        Run(console, "new", "-n", "Demo", "--presentation", "blazor-server", "--auth", "external", "--yes");

        Plain(console).ShouldContain("Presentation projects: blazor-server, worker (the worker added for you: the jobs run in it)");
        _processes.CommandLines.ShouldContain(line => line.StartsWith("dotnet new cleanarch-webapi -n Demo -o ", StringComparison.Ordinal) && line.Contains("--presentation blazor-server --presentation worker", StringComparison.Ordinal));
    }

    [Fact]
    public void APortalWithAWebApp_IsAnsweredTheApi_WhichTheWebAppCalls()
    {
        TestConsole console = Terminal();
        console.Profile.Width = 240;
        console.Input.PushTextWithEnter("Demo");
        console.Input.PushKey(ConsoleKey.Spacebar);
        console.Input.PushKey(ConsoleKey.DownArrow);
        console.Input.PushKey(ConsoleKey.Spacebar);
        console.Input.PushKey(ConsoleKey.Enter);
        console.Input.PushKey(ConsoleKey.DownArrow);
        PushEnters(console, 13);

        Run(console, "new");

        string output = Plain(console);
        Order(output, "◊  Presentation projects\n│  blazor-server\n", "◊  First web app\n│  placeholder", "◊  Presentation projects\n│  api, blazor-server · the API added for you: the web app calls it", "Summary");
        output.ShouldNotContain("◊  First web app\n│  none");
        output.ShouldMatch(@"│ Presentation projects\s+│ api, blazor-server\s+│");
        _processes.CommandLines.ShouldContain(line => line.StartsWith("dotnet new cleanarch-webapi -n Demo -o ", StringComparison.Ordinal) && line.Contains("--presentation api --presentation blazor-server --web placeholder", StringComparison.Ordinal));
    }

    [Fact]
    public void WithoutColours_ATerminal_IsAskedPlainly()
    {
        TestConsole console = Terminal().Colors(ColorSystem.NoColors);
        console.Input.PushTextWithEnter("Demo");
        PushEnters(console, 11);

        Run(console, "new");

        string output = Plain(console);
        output.ShouldNotContain(Banner.Tagline);
        output.ShouldNotContain(Rail.Answered);
        output.ShouldNotContain("Summary");
        output.ShouldContain("Done. Next time, skip the questions with:");
        output.ShouldContain(_created);
    }

    [Fact]
    public void WithYes_NothingIsSetUp_AndTheOutputIsTheTemplateEnginesThenTheCommands()
    {
        TestConsole console = Terminal();

        Run(console, "new", "-n", "Demo", "--yes");

        string output = Plain(console);
        output.ShouldNotContain(Banner.Tagline);
        output.ShouldNotContain(Rail.Bar);
        output.ShouldNotContain(Rail.Answered);
        Order(output, _created, _bootstrapInstruction, "Done. The same with dotnet new:", "Next:", "cd Demo", BootstrapStep.Command);
        output.ShouldNotContain("```");
        output.ShouldNotContain(LauncherCommands.Invocation);
        _processes.Calls.ShouldNotContain(call => call.FileName == "git");
        _processes.CommandLines.ShouldNotContain(BootstrapStep.Command);
    }

    [Fact]
    public void WithYesAndSetup_EveryStepRuns_InPlainLines()
    {
        TestConsole console = Terminal();

        Run(console, "new", "-n", "Demo", "--yes", "--setup");

        string output = Plain(console);
        output.ShouldNotContain(Rail.Answered);
        Order(output, "First commit done.", "Bootstrap done.", "Bootstrap commit done.", "Copy .env done.", "Done. The same with dotnet new:", "Next", "cd Demo", "docker compose up -d --build --wait");
        output.ShouldNotContain(EnvironmentFileStep.Command);
        output.ShouldContain(_created);
        output.ShouldNotContain(_bootstrapInstruction, customMessage: "The template engine's instruction to run bootstrap is relayed while the launcher runs it.");
        output.ShouldNotContain(TemplateEngine.PostActionsHeading);
        _processes.CommandLines.ShouldContain("git commit -q -m chore: bootstrap -- .");
    }

    [Fact]
    public void InsideAProject_TheLauncherSaysItCreatesProjects_AndNamesTheToolThatAdds()
    {
        AGeneratedProject();
        TestConsole console = Terminal();
        console.Profile.Width = 240;

        Run(console, "new", "-n", "Demo", "--yes");

        Plain(console).ShouldContain("You're inside Acme.Shop. The launcher creates projects; to add a web app, a web package or a presentation project, run dotnet cleanarch add <item>.");
    }

    private static TestConsole Terminal()
    {
        var console = new TestConsole().Interactive().EmitAnsiSequences();
        console.Profile.Width = 120;
        return console;
    }

    private string AFolderWithFiles()
    {
        string folder = Directory.CreateDirectory(Path.Combine(_root, "my-shop")).FullName;
        Directory.CreateDirectory(Path.Combine(folder, ".git"));
        File.WriteAllText(Path.Combine(folder, ".git", "HEAD"), "ref: refs/heads/main");
        File.WriteAllText(Path.Combine(folder, "notes.txt"), string.Empty);
        Directory.CreateDirectory(Path.Combine(folder, "drafts"));
        return folder;
    }

    private static void PushEnters(TestConsole console, int count)
    {
        for (int index = 0; index < count; index++)
        {
            console.Input.PushKey(ConsoleKey.Enter);
        }
    }

    private static string Plain(TestConsole console) => Ansi().Replace(console.Output, string.Empty).Replace("\r", string.Empty, StringComparison.Ordinal);

    private static string Closing(string output)
    {
        int done = output.LastIndexOf(Rail.End + Rail.Gutter, StringComparison.Ordinal);
        done.ShouldBeGreaterThanOrEqualTo(0, "The run printed no closing block.");
        return output[done..];
    }

    private static void Order(string output, params string[] markers)
    {
        int position = -1;
        foreach (string marker in markers)
        {
            int found = output.IndexOf(marker, position + 1, StringComparison.Ordinal);
            found.ShouldBeGreaterThan(position, $"'{marker}' is missing or out of order.");
            position = found;
        }
    }

    [GeneratedRegex(@"\x1b\[[0-9;?]*[ -/]*[@-~]")]
    private static partial Regex Ansi();

    private ProcessResult Bootstrapped()
    {
        _duringBootstrap?.Invoke();
        return _bootstrap;
    }

    private void Run(TestConsole console, params string[] arguments) =>
        new Launcher(console, _root, interactive: true, _processes).Run([.. arguments, "--template-source", Repository.Root]).ShouldBe(0, Plain(console));

    private void AGeneratedProject()
    {
        File.WriteAllText(Path.Combine(_root, "Acme.Shop.slnx"), string.Empty);
        Directory.CreateDirectory(Path.Combine(_root, "tools"));
        File.WriteAllText(Path.Combine(_root, "tools", "Tasks.cs"), string.Empty);
        Directory.CreateDirectory(Path.Combine(_root, "src", "Presentation", "AS.Api"));
    }

    private ProcessResult Answer(ProcessCall call)
    {
        if (call.FileName == "git")
        {
            _initialised |= call.Arguments[0] == "init";
            return call.Arguments[0] switch
            {
                "rev-parse" => _initialised ? new ProcessResult(0, "true") : new ProcessResult(128, "fatal: not a git repository"),
                "config" => _identity ? new ProcessResult(0, "Ada Lovelace") : new ProcessResult(1, string.Empty),
                "status" => new ProcessResult(0, " A README.md"),
                _ => new ProcessResult(0, string.Empty),
            };
        }

        if (call.Arguments[0] == "tool")
        {
            return new ProcessResult(0, string.Empty);
        }

        if (call.Arguments[0] == "cleanarch")
        {
            return Bootstrapped();
        }

        int output = call.Arguments.ToList().IndexOf("-o");
        if (output > 0)
        {
            Directory.CreateDirectory(call.Arguments[output + 1]);
            File.WriteAllText(Path.Combine(call.Arguments[output + 1], "README.md"), _runIt);
            File.WriteAllText(Path.Combine(call.Arguments[output + 1], ".env.example"), "API_HOST_PORT=8080");
            return new ProcessResult(0, $"{_created}\nProcessing post-creation actions...\nDescription: Bootstrap the project.\n{_bootstrapInstruction}\n");
        }

        string[] instructions = call.Arguments[1] == "cleanarch-webapi" ? ["Description: Bootstrap the project.", _bootstrapInstruction] : [];
        return new ProcessResult(0, string.Join('\n', [_created, string.Empty, .. instructions.Length > 0 ? [TemplateEngine.PostActionsHeading, .. instructions] : Array.Empty<string>()]));
    }
}

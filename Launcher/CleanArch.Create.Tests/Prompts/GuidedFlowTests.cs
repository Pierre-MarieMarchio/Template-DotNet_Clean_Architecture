using System.Text.RegularExpressions;
using CleanArch.Create.Projects;
using CleanArch.Create.Prompts;
using Shouldly;
using Spectre.Console;
using Spectre.Console.Testing;
using Xunit;

namespace CleanArch.Create.Tests.Prompts;

public sealed partial class GuidedFlowTests : IDisposable
{
    private const string _created = "The template was created successfully.";
    private const string _runIt =
        """
        ## Run it

        You need Docker.

        ```bash
        dotnet run Tools/Tasks.cs bootstrap     # once, after a fresh clone; commit what it changes
        cp .env.example .env
        docker compose up -d --build --wait
        ```

        ## Everyday commands
        """;

    private readonly string _root = Directory.CreateTempSubdirectory("cleanarch-create-").FullName;
    private readonly FakeProcesses _processes;
    private bool _identity = true;
    private bool _initialised;
    private ProcessResult _bootstrap = new(0, string.Empty);
    private string? _failingTask;

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
        output.ShouldContain("/ ___|");
        output.ShouldContain(Banner.Tool);
        output.ShouldContain(Banner.Tagline);
        output.ShouldContain("◇  Project name\n│  Demo\n");
        output.ShouldContain("◇  Database\n│  postgresql\n");
        output.ShouldContain("◇  Set up the project now?\n│  First commit, Bootstrap, Bootstrap commit, Copy .env\n");
        Order(
            output,
            "┌  Create a project",
            "◇  Authentication",
            "◇  Database",
            "◇  First web app",
            "◇  Presentation project",
            "◆  Set up the project now?",
            "Summary",
            "◇  Generated Demo",
            "◇  First commit done",
            "◇  Bootstrap done",
            "◇  Bootstrap commit done",
            "◇  Copy .env done",
            "The same, without questions",
            "--setup",
            "└  Done. Now run:");
        output.ShouldContain("╭");
        output.ShouldMatch(@"│ Authentication\s+│ local\s+│");
        output.ShouldMatch(@"│ Output folder\s+│ ./Demo\s+│");
        output.ShouldMatch(@"│ Set up\s+│ First commit, Bootstrap, Bootstrap commit, Copy .env\s+│");
        string left = Closing(output);
        left.ShouldContain("     cd ./Demo");
        left.ShouldContain("     docker compose up -d --build --wait");
        left.ShouldNotContain(ProjectSetup.BootstrapCommand);
        left.ShouldNotContain(ProjectSetup.EnvironmentCommand);
        left.ShouldNotContain("```");
        output.ShouldNotContain(_created);
        _processes.CommandLines.ShouldContain(line => line.StartsWith("dotnet new cleanarch-webapi -n Demo -o ", StringComparison.Ordinal));
        File.Exists(Path.Combine(_root, "Demo", ".env")).ShouldBeTrue();
    }

    [Fact]
    public void ARefusedAnswer_IsAnsweredOnTheThread_WithTheRefusalThatLeavesIt()
    {
        TestConsole console = Terminal();
        console.Input.PushTextWithEnter("Demo");
        console.Input.PushKey(ConsoleKey.Enter);
        console.Input.PushKey(ConsoleKey.End);
        PushEnters(console, 10);

        Run(console, "new");

        string output = Plain(console);
        output.ShouldContain("◇  Example features\n│  none · --auth none refuses the example features");
        output.ShouldContain("◇  Files module\n│  No · --auth none refuses the Files module");
        output.ShouldMatch(@"│ Example features\s+│ none\s+│");
    }

    [Fact]
    public void ASummaryRefused_GoesBackToTheLastQuestion_WithItsAnswerKept()
    {
        TestConsole console = Terminal();
        console.Input.PushTextWithEnter("Demo");
        console.Input.PushKey(ConsoleKey.Enter);
        console.Input.PushKey(ConsoleKey.DownArrow);
        PushEnters(console, 9);
        console.Input.PushKey(ConsoleKey.DownArrow);
        console.Input.PushKey(ConsoleKey.Spacebar);
        console.Input.PushKey(ConsoleKey.Enter);
        console.Input.PushKey(ConsoleKey.RightArrow);
        PushEnters(console, 3);

        Run(console, "new");

        string output = Plain(console);
        Regex.Count(output, "Summary").ShouldBe(2);
        Regex.Count(output, "◇  Set up the project now\\?\n│  First commit, Bootstrap commit, Copy .env").ShouldBe(2);
        Regex.Count(output, "◇  Authentication").ShouldBe(1);
        output.ShouldNotContain("◇  Bootstrap done");
        _processes.CommandLines.ShouldContain(line => line.StartsWith("dotnet new cleanarch-webapi -n Demo -o ", StringComparison.Ordinal) && line.Contains("--auth external", StringComparison.Ordinal));
        _processes.CommandLines.ShouldNotContain("dotnet run Tools/Tasks.cs bootstrap");
        Closing(output).ShouldContain(ProjectSetup.BootstrapCommand);
    }

    [Fact]
    public void GoingBack_ReopensTheLastQuestionAsked_AndWhatFollowsIsOfferedAnew()
    {
        TestConsole console = Terminal();
        console.Input.PushKey(ConsoleKey.Escape);
        console.Input.PushTextWithEnter("Demo");
        console.Input.PushKey(ConsoleKey.Enter);
        console.Input.PushKey(ConsoleKey.End);
        console.Input.PushKey(ConsoleKey.Enter);
        console.Input.PushKey(ConsoleKey.Escape);
        console.Input.PushKey(ConsoleKey.Home);
        PushEnters(console, 11);

        Run(console, "new");

        string output = Plain(console);
        Order(output, "◇  Authentication\n│  none", "◇  Example features\n│  none ·", "◆  Database", "◇  Authentication\n│  local", "○ notes", "◇  Example features\n│  all");
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
        Order(output, "◇  First commit done", "▲  Bootstrap failed", "exited with code 1", "NU1301", "▲  Bootstrap commit skipped", "◇  Copy .env done", "└  Done. Now run:");
        string left = Closing(output);
        left.ShouldContain(ProjectSetup.BootstrapCommand);
        left.ShouldContain("git commit -m \"chore: bootstrap\"");
        left.ShouldNotContain(ProjectSetup.EnvironmentCommand);
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
        Order(output, "▲  First commit skipped", "git has no user.name nor user.email", "◇  Bootstrap done", "▲  Bootstrap commit skipped");
        Closing(output).ShouldContain("git config --global user.email \"<your email>\"");
        _processes.CommandLines.ShouldNotContain(line => line.StartsWith("git commit", StringComparison.Ordinal));
    }

    [Fact]
    public void AWebApp_ReadsTheProject_AndOffersAPublicApiNoAdministrators()
    {
        AGeneratedProject();
        TestConsole console = Terminal();
        console.Input.PushTextWithEnter("BackOffice");
        PushEnters(console, 4);

        Run(console, "web-app");

        string output = Plain(console);
        output.ShouldContain("┌  Add a web app to Acme.Shop");
        output.ShouldContain("●  Read from Acme.Shop: --workspace true");
        output.ShouldContain("◇  Audience\n│  public · --audience admin signs administrators in");
        output.ShouldNotContain("Set up the project now?");
        Order(output, "◇  App name", "◇  Framework", "Summary", "◇  Added BackOffice", "└  Done. By hand, in Src/Web/README.md:");
        _processes.CommandLines.ShouldContain(line => line.StartsWith("dotnet new cleanarch-webapp -n BackOffice", StringComparison.Ordinal));
    }

    [Fact]
    public void AWebApp_TakesNoSetup()
    {
        AGeneratedProject();
        var console = new TestConsole();

        new Launcher(console, _root, interactive: false, _processes).Run(["web-app", "-n", "BackOffice", "--framework", "react", "--yes", "--setup"]).ShouldBe(1);

        console.Output.ShouldContain("web-app takes no --setup");
    }

    [Fact]
    public void APresentationProject_ReadsTheProject_AsksItsName_ThenJoinsAndCreatesIt()
    {
        AGeneratedProject();
        TestConsole console = Terminal();
        console.Profile.Width = 240;
        PushEnters(console, 3);

        Run(console, "presentation");

        string output = Plain(console);
        output.ShouldContain("┌  Add a presentation project to Acme.Shop");
        output.ShouldContain("●  Read from Acme.Shop: --project-name Acme.Shop, --auth none, --examples none, --files false, --database postgresql, --agent-files false");
        output.ShouldContain("◇  Framework\n│  blazor-server\n");
        Order(output, "◇  Presentation name\n│  Portal", "Summary", "◇  Added Portal", "◇  Join done", "◇  Scaffold done", "The same, without questions", "└  Done.");
        Closing(output).ShouldContain("Commit Src/Presentation/Acme.Shop.Portal and its tests");
        _processes.CommandLines.Where(line => !line.StartsWith("dotnet new uninstall", StringComparison.Ordinal) && !line.StartsWith("dotnet new install", StringComparison.Ordinal)).ShouldBe(
        [
            "dotnet new cleanarch-presentation -n Portal --framework blazor-server --project-name Acme.Shop --auth none --examples none --files false --agent-files false",
            "dotnet run Tools/Tasks.cs presentation-join Acme.Shop.Portal",
            "dotnet run Tools/Tasks.cs presentation-scaffold Acme.Shop.Portal",
        ]);
    }

    [Fact]
    public void APresentationProject_WhoseJoinFails_IsNotCreated_AndItsEditsAreLeftByHand()
    {
        AGeneratedProject();
        _failingTask = "presentation-join";
        TestConsole console = Terminal();
        PushEnters(console, 3);

        Run(console, "presentation");

        string output = Plain(console);
        Order(output, "▲  Join failed", "occur 2 times", "dotnet run Tools/Tasks.cs presentation-join Acme.Shop.Portal", "▲  Scaffold skipped", "└  Done. By hand, in Src/Presentation/Acme.Shop.Portal/README.md:");
        Closing(output).ShouldContain("- in `.dockerignore`, after the line:");
        _processes.CommandLines.ShouldNotContain(line => line.Contains("presentation-scaffold", StringComparison.Ordinal) && line.StartsWith("dotnet run", StringComparison.Ordinal));
    }

    [Fact]
    public void APresentationProject_WhosePagesFail_SaysWhatToRunAgain()
    {
        AGeneratedProject();
        _failingTask = "presentation-scaffold";
        TestConsole console = Terminal();
        PushEnters(console, 3);

        Run(console, "presentation");

        string output = Plain(console);
        Order(output, "◇  Join done", "▲  Scaffold failed", "dotnet run Tools/Tasks.cs presentation-scaffold Acme.Shop.Portal", "└  Done. By hand, in Src/Presentation/Acme.Shop.Portal/README.md:");
        Closing(output).ShouldContain("Commit the project");
    }

    [Theory]
    [InlineData("Api", "names a project the template already writes")]
    [InlineData("portal", "a presentation name is PascalCase")]
    [InlineData("Portal", "exists: choose another name")]
    public void APresentationName_TheProjectCannotTake_IsRefusedBeforeAnythingIsInstalled(string name, string refusal)
    {
        AGeneratedProject();
        Directory.CreateDirectory(Path.Combine(_root, "Src", "Presentation", "Acme.Shop.Portal"));
        var console = new TestConsole();
        console.Profile.Width = 240;

        new Launcher(console, _root, interactive: false, _processes).Run(["presentation", "-n", name, "--yes", "--template-source", Repository.Root]).ShouldBe(1);

        console.Output.ShouldContain(refusal);
        _processes.Calls.ShouldBeEmpty();
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
        output.ShouldContain("Done. The same, without questions:");
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
        Order(output, _created, "Done. The same, without questions:", "Next", "cd Demo", "```bash", ProjectSetup.BootstrapCommand);
        _processes.Calls.ShouldNotContain(call => call.FileName == "git");
        _processes.CommandLines.ShouldNotContain("dotnet run Tools/Tasks.cs bootstrap");
    }

    [Fact]
    public void WithYesAndSetup_EveryStepRuns_InPlainLines()
    {
        TestConsole console = Terminal();

        Run(console, "new", "-n", "Demo", "--yes", "--setup");

        string output = Plain(console);
        output.ShouldNotContain(Rail.Answered);
        Order(output, "First commit done.", "Bootstrap done.", "Bootstrap commit done.", "Copy .env done.", "--yes --setup", "Next", "cd Demo", "docker compose up -d --build --wait");
        output.ShouldNotContain(ProjectSetup.EnvironmentCommand);
        _processes.CommandLines.ShouldContain("git commit -q -m chore: bootstrap -- .");
    }

    private static TestConsole Terminal()
    {
        var console = new TestConsole().Interactive().EmitAnsiSequences();
        console.Profile.Width = 120;
        return console;
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
        int done = output.LastIndexOf("└  Done.", StringComparison.Ordinal);
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

    private void Run(TestConsole console, params string[] arguments) =>
        new Launcher(console, _root, interactive: true, _processes).Run([.. arguments, "--template-source", Repository.Root]).ShouldBe(0, Plain(console));

    private void AGeneratedProject()
    {
        File.WriteAllText(Path.Combine(_root, "Acme.Shop.sln"), string.Empty);
        Directory.CreateDirectory(Path.Combine(_root, "Tools"));
        File.WriteAllText(Path.Combine(_root, "Tools", "Tasks.cs"), string.Empty);
    }

    private static void APresentationReadme(ProcessCall call)
    {
        string folder = Directory.CreateDirectory(Path.Combine(call.WorkingDirectory, "Src", "Presentation", $"Acme.Shop.{call.Arguments[3]}")).FullName;
        File.WriteAllText(
            Path.Combine(folder, "README.md"),
            """
            # Portal

            ## Create the project

            ```bash
            dotnet run Tools/Tasks.cs presentation-scaffold Acme.Shop.Portal
            ```

            Commit the project.

            ## Joining a project generated without one

            - in `.dockerignore`, after the line:

              ```text
              !Src/Presentation/Acme.Shop.Api/appsettings.json
              ```
            """);
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

        if (call.Arguments[0] == "run")
        {
            return _failingTask is not null && call.Arguments.Contains(_failingTask)
                ? new ProcessResult(1, ".dockerignore: the lines to place the edit by occur 2 times, not once, so no file was changed:")
                : _bootstrap;
        }

        if (call.Arguments[1] == "cleanarch-presentation")
        {
            APresentationReadme(call);
        }

        int output = call.Arguments.ToList().IndexOf("-o");
        if (output > 0)
        {
            Directory.CreateDirectory(call.Arguments[output + 1]);
            File.WriteAllText(Path.Combine(call.Arguments[output + 1], "README.md"), _runIt);
            File.WriteAllText(Path.Combine(call.Arguments[output + 1], ".env.example"), "API_HOST_PORT=8080");
        }

        return new ProcessResult(0, _created);
    }
}

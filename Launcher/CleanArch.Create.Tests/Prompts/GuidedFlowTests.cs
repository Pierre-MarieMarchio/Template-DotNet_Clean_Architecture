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
    private const string _bootstrapInstruction = "Manual instructions: run dotnet run Tools/Tasks.cs bootstrap.";
    private const string _presentationInstruction = "Manual instructions: run presentation-join, then presentation-scaffold.";
    private const string _joinInstruction = "Manual instructions: run web-join.";
    private const string _scaffoldInstruction = "Manual instructions: run web-scaffold.";
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

    private const string _wgl4AboveLatin1 = "─│┌┐└┘├┤┬┴┼═║╒╓╔╕╖╗╘╙╚╛╜╝╞╟╠╡╢╣╤╥╦╧╨╩╪╫╬▀▄█▌▐░▒▓■□▪▫▬▲►▼◄◊○●◘◙◦♦←↑→↓–—‘’“”…";

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
        PushEnters(console, 13);

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
            "◊  Authentication",
            "◊  Database",
            "◊  First web app",
            "◊  Presentation project",
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
        left.ShouldNotContain(ProjectSetup.BootstrapCommand);
        left.ShouldNotContain(ProjectSetup.EnvironmentCommand);
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
        PushEnters(console, 13);

        Run(console, "new");

        Plain(console).Where(glyph => glyph > 'ÿ' && !_wgl4AboveLatin1.Contains(glyph)).Distinct().ShouldBeEmpty(
            "Outside the WGL4 set, a terminal takes the glyph from a fallback font, which draws it off the rail.");
    }

    [Fact]
    public void AfterQuestions_TheLaunchersCommand_KeepsTheHiveGiven()
    {
        TestConsole console = Terminal();
        console.Input.PushTextWithEnter("Demo");
        PushEnters(console, 13);

        Run(console, "new", "--hive", "hive");

        string output = Plain(console);
        output.ShouldContain("dnx CleanArch.Create new -n Demo -o ./Demo --yes --setup");
        output.ShouldMatch(@"[^:]--hive\s+(│\s+)?hive");
        output.ShouldContain("--debug:custom-hive hive");
    }

    [Fact]
    public void AnOutputFolderWithFiles_IsRefusedAtItsQuestion_NotAfterTheSummary()
    {
        Directory.CreateDirectory(Path.Combine(_root, "Demo"));
        File.WriteAllText(Path.Combine(_root, "Demo", "kept.txt"), string.Empty);
        TestConsole console = Terminal();
        console.Input.PushTextWithEnter("Demo");
        console.Input.PushKey(ConsoleKey.Enter);
        console.Input.PushTextWithEnter("./Other");
        PushEnters(console, 12);

        Run(console, "new");

        string output = Plain(console);
        Order(output, "That folder already has files in it.", "◊  Output folder\n│  ./Other\n", "◊  Authentication");
        _processes.CommandLines.ShouldContain(line => line.StartsWith("dotnet new cleanarch-webapi -n Demo -o ", StringComparison.Ordinal) && line.Contains("Other", StringComparison.Ordinal));
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
        output.ShouldContain("◊  Example features\n│  none · the only option when Authentication is none\n");
        output.ShouldContain("◊  Files module\n│  No · the only option when Authentication is none\n");
        output.ShouldMatch(@"│ Example features\s+│ none\s+│");
    }

    [Fact]
    public void ASummaryRefused_GoesBackToTheLastQuestion_WithItsAnswerKept()
    {
        TestConsole console = Terminal();
        console.Input.PushTextWithEnter("Demo");
        console.Input.PushKey(ConsoleKey.Enter);
        console.Input.PushKey(ConsoleKey.DownArrow);
        PushEnters(console, 10);
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
        PushEnters(console, 12);

        Run(console, "new");

        string output = Plain(console);
        Order(output, "◊  Authentication\n│  none", "◊  Example features\n│  none ·", "♦  Database", "◊  Authentication\n│  local", "○ notes", "◊  Example features\n│  all");
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
        PushEnters(console, 13);

        Run(console, "new");

        string output = Plain(console);
        Order(output, "◊  First commit done", "▲  Bootstrap failed", "exited with code 1", "NU1301", "▲  Bootstrap commit skipped", "◊  Copy .env done", "└  Almost there. Now run:");
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
        PushEnters(console, 13);

        Run(console, "new");

        string output = Plain(console);
        Order(output, "▲  First commit skipped", "git doesn't know who you are yet: user.name and user.email aren't set.", "◊  Bootstrap done", "▲  Bootstrap commit skipped");
        Closing(output).ShouldContain("git config --global user.email \"<your email>\"");
        _processes.CommandLines.ShouldNotContain(line => line.StartsWith("git commit", StringComparison.Ordinal));
    }

    [Fact]
    public void AnAppNameTaken_IsRefusedAtItsQuestion()
    {
        AGeneratedProject();
        Directory.CreateDirectory(Path.Combine(_root, "Src", "Web", "apps", "BackOffice"));
        TestConsole console = Terminal();
        console.Input.PushTextWithEnter("BackOffice");
        console.Input.PushTextWithEnter("2");
        PushEnters(console, 5);

        Run(console, "web-app");

        Order(Plain(console), "There's already an app with that name.", "◊  App name\n│  BackOffice2\n");
        _processes.CommandLines.ShouldContain(line => line.StartsWith("dotnet new cleanarch-webapp -n BackOffice2", StringComparison.Ordinal));
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
        output.ShouldContain("●  Taken from Acme.Shop: --workspace true");
        output.ShouldContain("◊  Audience\n│  public · the only option when Project authentication is none\n");
        output.ShouldNotContain("Set up the project now?");
        Order(output, "◊  App name", "◊  Framework", "Summary", "◊  Added BackOffice", "└  All set. A few things to do by hand, from Src/Web/README.md:");
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
    public void ASecondBlazorWebAssemblyApp_IsRefusedBeforeAnythingIsInstalled()
    {
        AGeneratedProject();
        File.WriteAllText(Path.Combine(_root, "Directory.Packages.props"), """<PackageVersion Include="Microsoft.AspNetCore.Authentication.JwtBearer" Version="10.0.13" />""");
        Directory.CreateDirectory(Path.Combine(_root, "Src", "Web", "apps", "Wasm"));
        File.WriteAllText(Path.Combine(_root, "Src", "Web", "apps", "Wasm", "Acme.Shop.Wasm.csproj"), string.Empty);
        var console = new TestConsole();
        console.Profile.Width = 240;

        new Launcher(console, _root, interactive: false, _processes).Run(["web-app", "-n", "Second", "--framework", "blazor-wasm", "--yes", "--template-source", Repository.Root]).ShouldBe(1);

        console.Output.ShouldContain("holds a Blazor WebAssembly app already, Src/Web/apps/Wasm");
        _processes.CommandLines.ShouldNotContain(line => line.StartsWith("dotnet new cleanarch-webapp", StringComparison.Ordinal));
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
        output.ShouldContain("●  Taken from Acme.Shop: --project-name Acme.Shop, --auth none, --examples none, --files false, --database postgresql, --agent-files false");
        output.ShouldContain("◊  Framework\n│  blazor-server · the only option when Project authentication is none");
        Order(output, "◊  Presentation name\n│  Portal", "Summary", "◊  Added Portal", "◊  Join done", "◊  Scaffold done", "Next time, skip the questions with", "└  All set.");
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
        Order(output, "▲  Join failed", "occur 2 times", "dotnet run Tools/Tasks.cs presentation-join Acme.Shop.Portal", "▲  Scaffold skipped", "└  All set. A few things to do by hand, from Src/Presentation/Acme.Shop.Portal/README.md:");
        Closing(output).ShouldContain("- in .dockerignore, after the line:");
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
        Order(output, "◊  Join done", "▲  Scaffold failed", "dotnet run Tools/Tasks.cs presentation-scaffold Acme.Shop.Portal", "└  All set. A few things to do by hand, from Src/Presentation/Acme.Shop.Portal/README.md:");
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
        PushEnters(console, 12);

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
        Order(output, _created, _bootstrapInstruction, "Done. The same with dotnet new:", "Next:", "cd Demo", ProjectSetup.BootstrapCommand);
        output.ShouldNotContain("```");
        output.ShouldNotContain(Launcher.Invocation);
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
        Order(output, "First commit done.", "Bootstrap done.", "Bootstrap commit done.", "Copy .env done.", "Done. The same with dotnet new:", "Next", "cd Demo", "docker compose up -d --build --wait");
        output.ShouldNotContain(ProjectSetup.EnvironmentCommand);
        output.ShouldContain(_created);
        output.ShouldNotContain(_bootstrapInstruction, customMessage: "The template engine's instruction to run bootstrap is relayed while the launcher runs it.");
        output.ShouldNotContain(TemplateEngine.PostActionsHeading);
        _processes.CommandLines.ShouldContain("git commit -q -m chore: bootstrap -- .");
    }

    [Fact]
    public void WithYes_APresentationProject_LeavesOutTheInstructionsToRunTheTasksItRuns()
    {
        AGeneratedProject();
        TestConsole console = Terminal();

        Run(console, "presentation", "--yes");

        string output = Plain(console);
        Order(output, _created, "Join done.", "Scaffold done.");
        output.ShouldNotContain(_presentationInstruction, customMessage: "The template engine's instruction to run presentation-join and presentation-scaffold is relayed while the launcher runs them.");
    }

    [Fact]
    public void WithYes_AWebAppItJoins_LeavesOutOnlyTheInstructionToJoinIt()
    {
        AGeneratedProject();
        File.WriteAllText(Path.Combine(_root, "Directory.Packages.props"), """<PackageVersion Include="Microsoft.AspNetCore.Authentication.JwtBearer" Version="10.0.13" />""");
        Directory.CreateDirectory(Path.Combine(_root, "Src", "Web", "apps"));
        File.WriteAllText(Path.Combine(_root, "Src", "Web", "package.json"), "{}");
        TestConsole console = Terminal();
        console.Profile.Width = 240;

        Run(console, "web-app", "-n", "Wasm", "--framework", "blazor-wasm", "--yes");

        string output = Plain(console);
        Order(output, _created, TemplateEngine.PostActionsHeading, _scaffoldInstruction, "Join done.");
        output.ShouldNotContain(_joinInstruction, customMessage: "The template engine's instruction to run web-join is relayed while the launcher runs it.");
        _processes.CommandLines.ShouldContain("dotnet run Tools/Tasks.cs web-join Wasm");
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

        if (call.Arguments[1] == "cleanarch-webapp")
        {
            string app = Directory.CreateDirectory(Path.Combine(call.WorkingDirectory, "Src", "Web", "apps", call.Arguments[3])).FullName;
            File.WriteAllText(Path.Combine(app, "README.md"), "# App\n\n## Join the stack\n\nInclude it.\n\n" + Launcher.JoiningSection + "\n\n- an edit\n");
        }

        int output = call.Arguments.ToList().IndexOf("-o");
        if (output > 0)
        {
            Directory.CreateDirectory(call.Arguments[output + 1]);
            File.WriteAllText(Path.Combine(call.Arguments[output + 1], "README.md"), _runIt);
            File.WriteAllText(Path.Combine(call.Arguments[output + 1], ".env.example"), "API_HOST_PORT=8080");
            return new ProcessResult(0, $"{_created}\nProcessing post-creation actions...\nDescription: Bootstrap the project.\n{_bootstrapInstruction}\n");
        }

        string[] instructions = call.Arguments[1] switch
        {
            "cleanarch-webapi" => ["Description: Bootstrap the project.", _bootstrapInstruction],
            "cleanarch-presentation" => ["Description: Join the project.", _presentationInstruction],
            "cleanarch-webapp" => ["Description: Join the app.", _joinInstruction, string.Empty, "Description: Create the app.", _scaffoldInstruction],
            _ => [],
        };
        return new ProcessResult(0, string.Join('\n', [_created, string.Empty, .. instructions.Length > 0 ? [TemplateEngine.PostActionsHeading, .. instructions] : Array.Empty<string>()]));
    }
}

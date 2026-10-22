using System.Text.RegularExpressions;
using CleanArch.Create.Prompts;
using Shouldly;
using Spectre.Console;
using Spectre.Console.Testing;
using Xunit;

namespace CleanArch.Create.Tests.Prompts;

public sealed partial class GuidedFlowTests : IDisposable
{
    private const string _runIt =
        """
        ## Run it

        You need Docker.

        ```bash
        dotnet run Tools/Tasks.cs bootstrap
        docker compose up -d --build --wait
        ```

        ## Everyday commands
        """;

    private readonly string _root = Directory.CreateTempSubdirectory("cleanarch-create-").FullName;
    private readonly FakeProcesses _processes;

    public GuidedFlowTests()
    {
        _processes = new FakeProcesses(call =>
        {
            int output = call.Arguments.ToList().IndexOf("-o");
            if (output > 0)
            {
                Directory.CreateDirectory(call.Arguments[output + 1]);
                File.WriteAllText(Path.Combine(call.Arguments[output + 1], "README.md"), _runIt);
            }

            return new ProcessResult(0, "The template was created successfully.");
        });
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void ANewProject_IsABannerThenAThread_ASummary_AndTheCommandsLeft()
    {
        TestConsole console = Terminal();
        console.Input.PushTextWithEnter("Demo");
        PushEnters(console, 10);

        new Launcher(console, _root, interactive: true, _processes).Run(["new", "--template-source", Repository.Root]).ShouldBe(0, Plain(console));

        string output = Plain(console);
        output.ShouldContain("/ ___|");
        output.ShouldContain(Banner.Tool);
        output.ShouldContain(Banner.Tagline);
        output.ShouldContain("┌  Create a project");
        output.ShouldContain("◇  Project name\n│  Demo\n");
        output.ShouldContain("◇  Database\n│  postgresql\n");
        Order(output, "┌  Create a project", "◇  Authentication", "◇  Database", "◇  First web app", "Summary", "◇  Generated Demo", "The same, without questions", "└  Done. Now run:");
        output.ShouldContain("╭");
        output.ShouldMatch(@"│ Authentication\s+│ local\s+│");
        output.ShouldMatch(@"│ Output folder\s+│ ./Demo\s+│");
        output.ShouldContain("     cd ./Demo");
        output.ShouldContain("     docker compose up -d --build --wait");
        output.ShouldNotContain("```");
        output.ShouldNotContain("The template was created successfully.");
        _processes.CommandLines.ShouldContain(line => line.StartsWith("dotnet new cleanarch-webapi -n Demo -o ", StringComparison.Ordinal));
    }

    [Fact]
    public void ARefusedAnswer_IsAnsweredOnTheThread_WithTheRefusalThatLeavesIt()
    {
        TestConsole console = Terminal();
        console.Input.PushTextWithEnter("Demo");
        console.Input.PushKey(ConsoleKey.Enter);
        console.Input.PushKey(ConsoleKey.End);
        PushEnters(console, 10);

        new Launcher(console, _root, interactive: true, _processes).Run(["new", "--template-source", Repository.Root]).ShouldBe(0, Plain(console));

        string output = Plain(console);
        output.ShouldContain("◇  Example features\n│  none · --auth none refuses the example features");
        output.ShouldContain("◇  Files module\n│  No · --auth none refuses the Files module");
        output.ShouldMatch(@"│ Example features\s+│ none\s+│");
    }

    [Fact]
    public void ASummaryRefused_AsksAgain_WithThePreviousAnswersAsDefaults()
    {
        TestConsole console = Terminal();
        console.Input.PushTextWithEnter("Demo");
        console.Input.PushKey(ConsoleKey.Enter);
        console.Input.PushKey(ConsoleKey.DownArrow);
        PushEnters(console, 8);
        console.Input.PushKey(ConsoleKey.RightArrow);
        PushEnters(console, 12);

        new Launcher(console, _root, interactive: true, _processes).Run(["new", "--template-source", Repository.Root]).ShouldBe(0, Plain(console));

        Regex.Count(Plain(console), "Summary").ShouldBe(2);
        _processes.CommandLines.ShouldContain(line => line.StartsWith("dotnet new cleanarch-webapi -n Demo -o ", StringComparison.Ordinal) && line.Contains("--auth external", StringComparison.Ordinal));
    }

    [Fact]
    public void AWebApp_ReadsTheProject_AndOffersAPublicApiNoAdministrators()
    {
        File.WriteAllText(Path.Combine(_root, "Acme.Shop.sln"), string.Empty);
        Directory.CreateDirectory(Path.Combine(_root, "Tools"));
        File.WriteAllText(Path.Combine(_root, "Tools", "Tasks.cs"), string.Empty);
        TestConsole console = Terminal();
        console.Input.PushTextWithEnter("BackOffice");
        PushEnters(console, 4);

        new Launcher(console, _root, interactive: true, _processes).Run(["web-app", "--template-source", Repository.Root]).ShouldBe(0, Plain(console));

        string output = Plain(console);
        output.ShouldContain("┌  Add a web app to Acme.Shop");
        output.ShouldContain("●  Read from Acme.Shop: --workspace true");
        output.ShouldContain("◇  Audience\n│  public · --audience admin signs administrators in");
        Order(output, "◇  App name", "◇  Framework", "Summary", "◇  Added BackOffice", "└  Done. By hand, in Src/Web/README.md:");
        _processes.CommandLines.ShouldContain(line => line.StartsWith("dotnet new cleanarch-webapp -n BackOffice", StringComparison.Ordinal));
    }

    [Fact]
    public void WithoutColours_ATerminal_IsAskedPlainly()
    {
        TestConsole console = Terminal().Colors(ColorSystem.NoColors);
        console.Input.PushTextWithEnter("Demo");
        PushEnters(console, 9);

        new Launcher(console, _root, interactive: true, _processes).Run(["new", "--template-source", Repository.Root]).ShouldBe(0, Plain(console));

        string output = Plain(console);
        output.ShouldNotContain(Banner.Tagline);
        output.ShouldNotContain(Rail.Answered);
        output.ShouldNotContain("Summary");
        output.ShouldContain("Done. The same, without questions:");
        output.ShouldContain("The template was created successfully.");
    }

    [Fact]
    public void WithYes_TheOutputIsTheTemplateEnginesThenTheCommands_WithNoRail()
    {
        TestConsole console = Terminal();

        new Launcher(console, _root, interactive: true, _processes).Run(["new", "-n", "Demo", "--yes", "--template-source", Repository.Root]).ShouldBe(0, Plain(console));

        string output = Plain(console);
        output.ShouldNotContain(Banner.Tagline);
        output.ShouldNotContain(Rail.Bar);
        output.ShouldNotContain(Rail.Answered);
        Order(output, "The template was created successfully.", "Done. The same, without questions:", "Next", "cd Demo", "```bash");
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
}

using System.Text.RegularExpressions;
using CleanArch.Tooling.Manifests;
using CleanArch.Tooling.Prompts;
using Shouldly;
using Spectre.Console.Testing;
using Xunit;

namespace CleanArch.Tooling.Tests.Prompts;

public sealed partial class FirstFrameTests
{
    private const string _hideCursor = "\u001b[?25l";

    private static readonly OptionChoice[] _databases =
    [
        new("postgresql", "PostgreSQL."),
        new("sqlserver", "SQL Server."),
        new("sqlite", "SQLite, for development only."),
    ];

    private readonly TestConsole _console = Terminal();

    [Fact]
    public void ASelection_ShowsEveryChoice_TheDefaultSelected_AndItsKeys_BeforeAnyKey()
    {
        Unanswered(() => new RailSelection("Database", "The database.", _databases, "sqlserver", inline: false, canGoBack: false, _ => null).Show(_console));

        string frame = Plain(_console);
        frame.ShouldContain("♦  Database");
        frame.ShouldContain("○ postgresql");
        frame.ShouldContain("● sqlserver");
        frame.ShouldContain("○ sqlite");
        frame.ShouldContain("↑/↓ move · enter select · first letter jumps");
    }

    [Fact]
    public void AYesNo_ShowsBothAnswers_AndItsDefault_BeforeAnyKey()
    {
        Unanswered(() => new RailSelection("Files module", string.Empty, [new(Conversation.Yes, string.Empty), new(Conversation.No, string.Empty)], Conversation.No, inline: true, canGoBack: false, _ => null).Show(_console));

        string frame = Plain(_console);
        frame.ShouldContain("○ Yes / ● No");
        frame.ShouldContain("y/n · enter = No");
    }

    [Fact]
    public void AMultiSelection_ShowsEveryStep_Checked_AndItsKeys_BeforeAnyKey()
    {
        Unanswered(() => new RailMultiSelection("Set up the project now?", string.Empty, _databases, [.. _databases.Select(choice => choice.Value)], atLeastOne: false, canGoBack: false).Show(_console));

        string frame = Plain(_console);
        frame.ShouldContain("■ postgresql");
        frame.ShouldContain("■ sqlite");
        frame.ShouldContain("↑/↓ move · space toggle · a all · enter confirm");
    }

    [Fact]
    public void AText_ShowsItsDefault_AndItsKeys_BeforeAnyKey()
    {
        Unanswered(() => new RailText("Web app name", null, "Site", canGoBack: false, _ => null).Show(_console));

        string frame = Plain(_console);
        frame.ShouldContain("♦  Web app name");
        frame.ShouldContain("Site");
        frame.ShouldContain("type a value · enter keeps Site");
    }

    private static TestConsole Terminal()
    {
        var console = new TestConsole().Interactive().EmitAnsiSequences();
        console.Profile.Width = 120;
        return console;
    }

    private static string Plain(TestConsole console) => Ansi().Replace(console.Output, string.Empty).Replace("\r", string.Empty, StringComparison.Ordinal);

    private void Unanswered(Action show)
    {
        Should.Throw<InvalidOperationException>(show);
        _console.Output.ShouldStartWith(_hideCursor);
    }

    [GeneratedRegex(@"\x1b\[[0-9;?]*[ -/]*[@-~]")]
    private static partial Regex Ansi();
}

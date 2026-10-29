using CleanArch.Tooling.Manifests;
using CleanArch.Tooling.Prompts;
using Shouldly;
using Spectre.Console.Testing;
using Xunit;

namespace CleanArch.Tooling.Tests.Prompts;

public sealed class BackKeyTests
{
    private static readonly OptionChoice[] _choices = [new("postgresql", "PostgreSQL."), new("sqlite", "SQLite.")];
    private static readonly OptionChoice[] _yesNo = [new(Conversation.Yes, string.Empty), new(Conversation.No, string.Empty)];

    private readonly TestConsole _console = new TestConsole().Interactive().EmitAnsiSequences();

    [Theory]
    [InlineData(ConsoleKey.Escape)]
    [InlineData(ConsoleKey.LeftArrow)]
    public void ASelection_GoesBack_OnEscapeOrLeft_AndSaysSo(ConsoleKey key)
    {
        _console.Input.PushKey(key);

        Selection(canGoBack: true).Show(_console).ShouldBeNull();

        _console.Output.ShouldContain("↑/↓ move · enter select · first letter jumps · ←/esc back");
    }

    [Fact]
    public void TheFirstQuestion_HasNoBack_AndDoesNotOfferOne()
    {
        _console.Input.PushKey(ConsoleKey.Escape);
        _console.Input.PushKey(ConsoleKey.Enter);

        Selection(canGoBack: false).Show(_console).ShouldBe("postgresql");

        _console.Output.ShouldNotContain("esc back");
    }

    [Fact]
    public void AYesNo_MovesOnLeft_AndGoesBackOnEscape()
    {
        _console.Input.PushKey(ConsoleKey.LeftArrow);
        _console.Input.PushKey(ConsoleKey.Enter);
        new RailSelection("Files module", string.Empty, _yesNo, Conversation.No, inline: true, canGoBack: true, _ => null).Show(_console).ShouldBe(Conversation.Yes);

        _console.Input.PushKey(ConsoleKey.Escape);
        new RailSelection("Files module", string.Empty, _yesNo, Conversation.No, inline: true, canGoBack: true, _ => null).Show(_console).ShouldBeNull();

        _console.Output.ShouldContain("y/n · enter = No · esc back");
    }

    [Fact]
    public void AText_GoesBack_OnBackspaceOnceEmpty()
    {
        _console.Input.PushText("ab");
        _console.Input.PushKey(ConsoleKey.Backspace);
        _console.Input.PushKey(ConsoleKey.Backspace);
        _console.Input.PushKey(ConsoleKey.Backspace);

        new RailText("Web app name", null, "Site", canGoBack: true, _ => null).Show(_console).ShouldBeNull();

        _console.Output.ShouldContain("type a value · enter keeps Site · esc back");
    }

    [Fact]
    public void AMultiSelection_GoesBack_OnEscape()
    {
        _console.Input.PushKey(ConsoleKey.Escape);

        new RailMultiSelection("Set up the project now?", _choices, ["postgresql"], canGoBack: true).Show(_console).ShouldBeNull();

        _console.Output.ShouldContain("enter confirm · ←/esc back");
    }

    private static RailSelection Selection(bool canGoBack) =>
        new("Database", string.Empty, _choices, null, inline: false, canGoBack, _ => null);
}

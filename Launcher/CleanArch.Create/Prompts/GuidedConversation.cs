using CleanArch.Create.Manifests;
using Spectre.Console;

namespace CleanArch.Create.Prompts;

public sealed class GuidedConversation : Conversation
{
    private const string _fence = "```";

    private static readonly IReadOnlyDictionary<string, string> _accents = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["react"] = "cyan",
        ["angular"] = "red",
        ["placeholder"] = "grey",
    };

    private static readonly IReadOnlyList<OptionChoice> _yesNo = [new(Yes, string.Empty), new(No, string.Empty)];

    private readonly IAnsiConsole _console;
    private bool _begun;

    public GuidedConversation(IAnsiConsole console)
    {
        _console = console;
    }

    public override bool EchoesTheTemplateEngine => false;

    public override void Begin(string title, string? version)
    {
        if (_begun)
        {
            return;
        }

        _begun = true;
        Banner.Write(_console, version);
        Mark(Rail.Start, "grey", Markup.Escape(title));
        Gap();
    }

    public override string Choose(string label, string description, IReadOnlyList<OptionChoice> choices, string? defaultValue)
    {
        string selected = new RailSelection(label, description, choices, defaultValue, inline: false, _accents.GetValueOrDefault).Show(_console);
        Recall(label, selected, null);
        return selected;
    }

    public override bool Confirm(string label, string description, bool defaultValue)
    {
        string selected = new RailSelection(label, description, _yesNo, defaultValue ? Yes : No, inline: true, _ => null).Show(_console);
        Recall(label, selected, null);
        return selected == Yes;
    }

    public override string Text(string label, string? description, string? defaultValue, bool allowEmpty, Func<string, string?> problem)
    {
        string text = new RailText(label, description, defaultValue, value =>
            value.Length == 0 && !allowEmpty && string.IsNullOrEmpty(defaultValue) ? "An answer is required." : problem(value)).Show(_console);
        Recall(label, text.Length == 0 ? "(none)" : text, null);
        return text;
    }

    public override void Answered(string label, string value, string reason) => Recall(label, value, reason);

    public override void Warn(string text)
    {
        Mark(Rail.Warning, "yellow", $"[yellow]{Markup.Escape(text)}[/]");
        Gap();
    }

    public override void Note(string text)
    {
        Mark(Rail.Note, "grey", $"[grey]{Markup.Escape(text)}[/]");
        Gap();
    }

    public override bool Review(string subject, IReadOnlyList<KeyValuePair<string, string>> rows)
    {
        var table = new Table()
            .Border(TableBorder.Rounded)
            .BorderColor(Color.Grey)
            .AddColumn(new TableColumn("[grey]Option[/]"))
            .AddColumn(new TableColumn("[grey]Value[/]"));
        foreach ((string label, string value) in rows)
        {
            table.AddRow(new Markup($"[bold]{Markup.Escape(label)}[/]"), new Markup(Markup.Escape(value)));
        }

        Mark(Rail.Answered, "green", "[bold]Summary[/]");
        _console.Write(new Rail(table));
        Gap();

        return Confirm(subject + "?", string.Empty, defaultValue: true);
    }

    public override void Perform(string running, string done, Action work)
    {
        _console.Status()
            .Spinner(Spinner.Known.Dots)
            .SpinnerStyle(new Style(Color.Aqua))
            .Start(Markup.Escape(running), _ => work());
        Mark(Rail.Answered, "green", Markup.Escape(done));
        Gap();
    }

    public override void Equivalents(IReadOnlyList<string> commandLines)
    {
        Mark(Rail.Answered, "green", "The same, without questions");
        foreach (string line in commandLines)
        {
            _console.Write(Rail.Of($"[grey]{Markup.Escape(line)}[/]"));
        }

        Gap();
    }

    public override void Close(Closing? closing)
    {
        string heading = closing switch
        {
            null => "Done.",
            { Readme: null } => "Done. Now run:",
            { Readme: { } readme } => $"Done. By hand, in {readme}:",
        };
        Mark(Rail.End, "grey", $"[bold]{Markup.Escape(heading)}[/]");
        if (closing is null)
        {
            return;
        }

        _console.WriteLine();
        foreach (string command in closing.Commands)
        {
            Indented(command, "bold");
        }

        if (closing.Commands.Count > 0)
        {
            _console.WriteLine();
        }

        foreach ((string text, bool code) in Blocks(closing.Section ?? string.Empty))
        {
            Indented(text, code ? "bold" : "grey");
        }
    }

    public override void Fail(string message)
    {
        Mark(Rail.Warning, "red", $"[red]{Markup.Escape(message)}[/]");
        Mark(Rail.End, "red", "Stopped.");
    }

    private void Recall(string label, string value, string? reason)
    {
        Mark(Rail.Answered, "green", Markup.Escape(label));
        _console.Write(Rail.Of(reason is null
            ? $"[grey]{Markup.Escape(value)}[/]"
            : $"[grey]{Markup.Escape(value)}[/] [grey italic]· {Markup.Escape(reason)}[/]"));
        Gap();
    }

    private static IEnumerable<(string Text, bool Code)> Blocks(string markdown)
    {
        bool code = false;
        string? paragraph = null;
        foreach (string line in markdown.Split('\n').Select(line => line.TrimEnd('\r')))
        {
            bool fence = line.TrimStart().StartsWith(_fence, StringComparison.Ordinal);
            if (!code && !fence && line.Length > 0 && !line.StartsWith('|'))
            {
                paragraph = paragraph is null ? line : paragraph + " " + line;
                continue;
            }

            if (paragraph is not null)
            {
                yield return (paragraph, false);
                paragraph = null;
            }

            if (fence)
            {
                code = !code;
            }
            else
            {
                yield return (line, code);
            }
        }

        if (paragraph is not null)
        {
            yield return (paragraph, false);
        }
    }

    private void Mark(string glyph, string colour, string markup)
    {
        _console.Write(Rail.Line(glyph, colour, markup));
        _console.WriteLine();
    }

    private void Gap()
    {
        _console.Write(Rail.Empty());
        _console.WriteLine();
    }

    private void Indented(string text, string style)
    {
        if (text.Length == 0)
        {
            _console.WriteLine();
            return;
        }

        _console.Write(new Padder(new Markup($"[{style}]{Markup.Escape(text)}[/]"), new Padding(5, 0, 0, 0)));
    }
}

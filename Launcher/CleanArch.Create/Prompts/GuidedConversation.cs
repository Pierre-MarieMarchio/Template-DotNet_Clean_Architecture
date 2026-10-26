using CleanArch.Create.Manifests;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace CleanArch.Create.Prompts;

public sealed class GuidedConversation : Conversation
{
    private const string _eraseBelow = "\u001b[0J";

    private static readonly IReadOnlyDictionary<string, string> _accents = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["react"] = "cyan",
        ["vite-react"] = "cyan",
        ["angular"] = "red",
        ["vue"] = "green",
        ["vite-vue"] = "green",
        ["vite-svelte"] = "orangered1",
        ["react-router"] = "indianred1",
        ["placeholder"] = "grey",
        ["blazor-wasm"] = "mediumorchid",
        ["blazor-server"] = "mediumpurple",
        ["avalonia"] = "slateblue1",
        ["wpf"] = "dodgerblue1",
        ["mcp"] = "darkorange",
    };

    private static readonly IReadOnlyList<OptionChoice> _yesNo = [new(Yes, string.Empty), new(No, string.Empty)];

    private readonly IAnsiConsole _console;
    private bool _begun;
    private int _lines;

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
        Line(Rail.Start, "grey", Markup.Escape(title));
        Gap();
    }

    public override string? Choose(string label, string description, IReadOnlyList<OptionChoice> choices, string? defaultValue)
    {
        string? selected = new RailSelection(label, description, choices, defaultValue, inline: false, CanGoBack, _accents.GetValueOrDefault).Show(_console);
        if (selected is not null)
        {
            Recall(label, selected, null);
        }

        return selected;
    }

    public override IReadOnlyList<string>? ChooseMany(string label, IReadOnlyList<OptionChoice> choices, IReadOnlyCollection<string> selected)
    {
        IReadOnlyList<string>? chosen = new RailMultiSelection(label, choices, selected, CanGoBack).Show(_console);
        if (chosen is not null)
        {
            Recall(label, chosen.Count == 0 ? "Nothing" : string.Join(", ", chosen), null);
        }

        return chosen;
    }

    public override bool? Confirm(string label, string description, bool defaultValue)
    {
        string? selected = new RailSelection(label, description, _yesNo, defaultValue ? Yes : No, inline: true, CanGoBack, _ => null).Show(_console);
        if (selected is null)
        {
            return null;
        }

        Recall(label, selected, null);
        return selected == Yes;
    }

    public override string? Text(string label, string? description, string? defaultValue, bool allowEmpty, Func<string, string?> problem)
    {
        string? text = new RailText(label, description, defaultValue, CanGoBack, value =>
            value.Length == 0 && !allowEmpty && string.IsNullOrEmpty(defaultValue) ? "An answer is required." : problem(value)).Show(_console);
        if (text is not null)
        {
            Recall(label, text.Length == 0 ? "(none)" : text, null);
        }

        return text;
    }

    public override void Answered(string label, string value, string reason) => Recall(label, value, reason);

    public override void Warn(string text)
    {
        Line(Rail.Warning, "yellow", $"[yellow]{Markup.Escape(text)}[/]");
        Gap();
    }

    public override void Note(string text)
    {
        Line(Rail.Note, "grey", $"[grey]{Markup.Escape(text)}[/]");
        Gap();
    }

    public override bool Review(string subject, IReadOnlyList<KeyValuePair<string, string>> rows)
    {
        var table = new Table()
            .Border(TableBorder.Square)
            .BorderColor(Color.Grey)
            .AddColumn(new TableColumn("[grey]Option[/]"))
            .AddColumn(new TableColumn("[grey]Value[/]"));
        foreach ((string label, string value) in rows)
        {
            table.AddRow(new Markup($"[bold]{Markup.Escape(label)}[/]"), new Markup(Markup.Escape(value)));
        }

        Line(Rail.Answered, "green", "[bold]Summary[/]");
        Railed(table);
        Gap();

        return Confirm(subject + "?", string.Empty, defaultValue: true) == true;
    }

    public override int Mark() => _lines;

    public override void Rewind(int mark)
    {
        if (_lines <= mark)
        {
            return;
        }

        _console.Cursor.MoveUp(_lines - mark);
        _console.WriteAnsi(_eraseBelow);
        _lines = mark;
    }

    public override void Perform(string running, string done, Action work)
    {
        Spin(running, () =>
        {
            work();
            return done;
        });
        Line(Rail.Answered, "green", Markup.Escape(done));
        Gap();
    }

    public override T Spin<T>(string running, Func<T> work) =>
        _console.Status()
            .Spinner(new RailSpinner())
            .SpinnerStyle(new Style(Color.Aqua))
            .Start(Markup.Escape(running), _ => work());

    public override void Reported(string text, bool succeeded, string? why, IReadOnlyList<string> rerun)
    {
        if (succeeded)
        {
            Line(Rail.Answered, "green", Markup.Escape(text));
        }
        else
        {
            Line(Rail.Warning, "yellow", $"[yellow]{Markup.Escape(text)}[/]");
        }

        if (why is not null)
        {
            Railed(new Markup($"[grey]{Markup.Escape(why)}[/]"));
        }

        foreach (string command in rerun)
        {
            Railed(new Markup($"[bold]{Markup.Escape(command)}[/]"));
        }

        Gap();
    }

    public override void Equivalents(IReadOnlyList<string> commandLines, bool asked)
    {
        Line(Rail.Answered, "green", "Next time, skip the questions with");
        foreach (string line in commandLines)
        {
            Railed(new Markup($"[grey]{Markup.Escape(line)}[/]"));
        }

        Gap();
    }

    public override void Close(Closing? closing)
    {
        string heading = closing switch
        {
            null => "Done.",
            { Readme: null, StepsLeft: true } => "Almost there. Now run:",
            { Readme: null } => "All set. Now run:",
            { Readme: { } readme } => $"All set. A few things to do by hand, from {readme}:",
        };
        Line(Rail.End, "grey", $"[bold]{Markup.Escape(heading)}[/]");
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

        foreach ((string text, bool code) in ReadmeText.Blocks(closing.Section ?? string.Empty))
        {
            Indented(text, code ? "bold" : "grey");
        }
    }

    public override void Fail(string message)
    {
        Line(Rail.Warning, "red", $"[red]{Markup.Escape(message)}[/]");
        Line(Rail.End, "red", "Stopped.");
    }

    private void Recall(string label, string value, string? reason)
    {
        Line(Rail.Answered, "green", Markup.Escape(label));
        Railed(new Markup(string.IsNullOrEmpty(reason)
            ? $"[grey]{Markup.Escape(value)}[/]"
            : $"[grey]{Markup.Escape(value)}[/] [grey italic]· {Markup.Escape(reason)}[/]"));
        Gap();
    }

    private void Line(string glyph, string colour, string markup)
    {
        Markup line = Rail.Line(glyph, colour, markup);
        _console.Write(line);
        _console.WriteLine();
        _lines += Lines(line, _console.Profile.Width);
    }

    private void Railed(IRenderable content)
    {
        _console.Write(new Rail(content));
        _lines += Lines(content, _console.Profile.Width - Rail.Width);
    }

    private void Gap()
    {
        _console.Write(Rail.Empty());
        _console.WriteLine();
        _lines++;
    }

    private int Lines(IRenderable content, int width) =>
        Segment.SplitLines(content.Render(RenderOptions.Create(_console), Math.Max(1, width))).Count;

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

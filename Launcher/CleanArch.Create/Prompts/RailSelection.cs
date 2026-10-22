using CleanArch.Create.Manifests;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace CleanArch.Create.Prompts;

public sealed class RailSelection
{
    private readonly string _label;
    private readonly string _description;
    private readonly IReadOnlyList<OptionChoice> _choices;
    private readonly bool _inline;
    private readonly Func<string, string?> _accent;
    private int _index;

    public RailSelection(string label, string description, IReadOnlyList<OptionChoice> choices, string? defaultValue, bool inline, Func<string, string?> accent)
    {
        _label = label;
        _description = description;
        _choices = choices;
        _inline = inline;
        _accent = accent;
        _index = Math.Max(0, choices.ToList().FindIndex(choice => choice.Value == defaultValue));
    }

    public string Show(IAnsiConsole console)
    {
        LiveDisplay live = console.Live(Render());
        live.AutoClear = true;
        live.Start(context =>
        {
            for (ConsoleKeyInfo key = RailKeys.Read(console); key.Key != ConsoleKey.Enter; key = RailKeys.Read(console))
            {
                _index = Moved(key);
                context.UpdateTarget(Render());
            }
        });

        return _choices[_index].Value;
    }

    private int Moved(ConsoleKeyInfo key) => key.Key switch
    {
        ConsoleKey.UpArrow or ConsoleKey.LeftArrow => (_index + _choices.Count - 1) % _choices.Count,
        ConsoleKey.DownArrow or ConsoleKey.RightArrow or ConsoleKey.Tab => (_index + 1) % _choices.Count,
        ConsoleKey.Home => 0,
        ConsoleKey.End => _choices.Count - 1,
        _ when char.IsLetterOrDigit(key.KeyChar) => Starting(key.KeyChar),
        _ => _index,
    };

    private int Starting(char initial)
    {
        int found = _choices.ToList().FindIndex(choice => choice.Value.StartsWith(initial.ToString(), StringComparison.OrdinalIgnoreCase));
        return found < 0 ? _index : found;
    }

    private Rows Render()
    {
        List<IRenderable> lines = [Rail.Line(Rail.Asking, "cyan", $"[bold]{Markup.Escape(_label)}[/]")];
        if (_description.Length > 0)
        {
            lines.Add(Rail.Of($"[grey]{Markup.Escape(_description)}[/]"));
        }

        lines.Add(new Rail(_inline ? Inline() : List()));
        lines.Add(Rail.Line(Rail.End, "cyan", string.Empty));

        return new Rows(lines);
    }

    private Markup Inline() =>
        new(string.Join(" [grey]/[/] ", _choices.Select((choice, index) => Marker(index) + " " + Value(choice.Value, index))));

    private Grid List()
    {
        var grid = new Grid()
            .AddColumn(new GridColumn().NoWrap().PadRight(1))
            .AddColumn(new GridColumn().NoWrap().PadRight(2))
            .AddColumn(new GridColumn());

        for (int index = 0; index < _choices.Count; index++)
        {
            grid.AddRow(
                new Markup(Marker(index)),
                new Markup(Value(_choices[index].Value, index)),
                new Markup($"[grey]{Markup.Escape(_choices[index].Description)}[/]"));
        }

        return grid;
    }

    private string Marker(int index) => index == _index ? "[cyan]●[/]" : "[grey]○[/]";

    private string Value(string value, int index)
    {
        string? accent = _accent(value);
        string text = Markup.Escape(value);
        if (index == _index)
        {
            return $"[bold {accent ?? "cyan"}]{text}[/]";
        }

        return accent is null ? text : $"[{accent}]{text}[/]";
    }
}

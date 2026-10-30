using CleanArch.Tooling.Manifests;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace CleanArch.Tooling.Prompts;

public sealed class RailMultiSelection
{
    private const string _noneChosen = "Choose at least one.";

    private readonly string _label;
    private readonly string _description;
    private readonly IReadOnlyList<OptionChoice> _choices;
    private readonly bool[] _checked;
    private readonly bool _atLeastOne;
    private readonly bool _canGoBack;
    private int _index;
    private string? _error;

    public RailMultiSelection(string label, string description, IReadOnlyList<OptionChoice> choices, IReadOnlyCollection<string> selected, bool atLeastOne, bool canGoBack)
    {
        _canGoBack = canGoBack;
        _label = label;
        _description = description;
        _choices = choices;
        _atLeastOne = atLeastOne;
        _checked = [.. choices.Select(choice => selected.Contains(choice.Value))];
    }

    public IReadOnlyList<string>? Show(IAnsiConsole console)
    {
        bool back = false;
        LiveDisplay live = console.Live(Render());
        live.AutoClear = true;
        live.Start(context =>
        {
            context.Refresh();
            for (ConsoleKeyInfo key = RailKeys.Read(console); !Confirmed(key); key = RailKeys.Read(console))
            {
                if (_canGoBack && key.Key is ConsoleKey.Escape or ConsoleKey.LeftArrow)
                {
                    back = true;
                    return;
                }

                Handle(key);
                context.UpdateTarget(Render());
            }
        });

        return back ? null : [.. _choices.Where((_, index) => _checked[index]).Select(choice => choice.Value)];
    }

    private bool Confirmed(ConsoleKeyInfo key)
    {
        _error = key.Key == ConsoleKey.Enter && _atLeastOne && !_checked.Contains(true) ? _noneChosen : null;
        return key.Key == ConsoleKey.Enter && _error is null;
    }

    private void Handle(ConsoleKeyInfo key)
    {
        switch (key.Key)
        {
            case ConsoleKey.UpArrow:
                _index = (_index + _choices.Count - 1) % _choices.Count;
                break;
            case ConsoleKey.DownArrow or ConsoleKey.Tab:
                _index = (_index + 1) % _choices.Count;
                break;
            case ConsoleKey.Spacebar:
                _checked[_index] = !_checked[_index];
                break;
            case ConsoleKey.A:
                bool all = !_checked.All(value => value);
                Array.Fill(_checked, all);
                break;
        }
    }

    private Rows Render()
    {
        var grid = new Grid()
            .AddColumn(new GridColumn().NoWrap().PadRight(1))
            .AddColumn(new GridColumn().NoWrap().PadRight(2))
            .AddColumn(new GridColumn());

        for (int index = 0; index < _choices.Count; index++)
        {
            string box = _checked[index] ? "[cyan]■[/]" : "[grey]□[/]";
            string value = Markup.Escape(_choices[index].Value);
            grid.AddRow(
                new Markup(box),
                new Markup(index == _index ? $"[bold cyan]{value}[/]" : value),
                new Markup($"[grey]{Markup.Escape(_choices[index].Description)}[/]"));
        }

        string colour = _error is null ? "cyan" : "yellow";
        List<IRenderable> lines = [Rail.Line(Rail.Asking, colour, $"[bold]{Markup.Escape(_label)}[/]")];
        if (_description.Length > 0)
        {
            lines.Add(Rail.Of($"[grey]{Markup.Escape(_description)}[/]"));
        }

        lines.Add(new Rail(grid));
        if (_error is not null)
        {
            lines.Add(Rail.Of($"[yellow]{Markup.Escape(_error)}[/]"));
        }

        lines.Add(Rail.Of($"[grey]↑/↓ move · space toggle · a all · enter confirm{(_canGoBack ? RailKeys.BackOrLeft : string.Empty)}[/]"));
        lines.Add(Rail.Line(Rail.End, colour, string.Empty));

        return new Rows(lines);
    }
}

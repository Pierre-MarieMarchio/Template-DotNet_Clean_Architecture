using CleanArch.Create.Manifests;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace CleanArch.Create.Prompts;

public sealed class RailMultiSelection
{
    private readonly string _label;
    private readonly IReadOnlyList<OptionChoice> _choices;
    private readonly bool[] _checked;
    private readonly bool _canGoBack;
    private int _index;

    public RailMultiSelection(string label, IReadOnlyList<OptionChoice> choices, IReadOnlyCollection<string> selected, bool canGoBack)
    {
        _canGoBack = canGoBack;
        _label = label;
        _choices = choices;
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
            for (ConsoleKeyInfo key = RailKeys.Read(console); key.Key != ConsoleKey.Enter; key = RailKeys.Read(console))
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

        List<IRenderable> lines =
        [
            Rail.Line(Rail.Asking, "cyan", $"[bold]{Markup.Escape(_label)}[/]"),
            new Rail(grid),
            Rail.Of($"[grey]↑/↓ move · space toggle · a all · enter confirm{(_canGoBack ? RailKeys.BackOrLeft : string.Empty)}[/]"),
            Rail.Line(Rail.End, "cyan", string.Empty),
        ];

        return new Rows(lines);
    }
}

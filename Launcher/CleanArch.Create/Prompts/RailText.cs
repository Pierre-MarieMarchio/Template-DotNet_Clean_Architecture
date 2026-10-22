using System.Text;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace CleanArch.Create.Prompts;

public sealed class RailText
{
    private const string _caret = "▌";

    private readonly string _label;
    private readonly string? _description;
    private readonly string? _defaultValue;
    private readonly Func<string, string?> _problem;
    private readonly StringBuilder _input = new();
    private string? _error;
    private string _answer = string.Empty;

    public RailText(string label, string? description, string? defaultValue, Func<string, string?> problem)
    {
        _label = label;
        _description = description;
        _defaultValue = defaultValue;
        _problem = problem;
    }

    public string Show(IAnsiConsole console)
    {
        LiveDisplay live = console.Live(Render());
        live.AutoClear = true;
        live.Start(context =>
        {
            for (ConsoleKeyInfo key = RailKeys.Read(console); !Accepted(key); key = RailKeys.Read(console))
            {
                context.UpdateTarget(Render());
            }
        });

        return _answer;
    }

    private bool Accepted(ConsoleKeyInfo key)
    {
        if (key.Key == ConsoleKey.Enter)
        {
            string value = _input.Length == 0 ? _defaultValue ?? string.Empty : _input.ToString();
            _error = _problem(value);
            _answer = value;
            return _error is null;
        }

        if (key.Key == ConsoleKey.Backspace && _input.Length > 0)
        {
            _input.Length--;
        }
        else if (!char.IsControl(key.KeyChar))
        {
            _input.Append(key.KeyChar);
        }

        _error = null;
        return false;
    }

    private Rows Render()
    {
        string colour = _error is null ? "cyan" : "yellow";
        List<IRenderable> lines = [Rail.Line(Rail.Asking, colour, $"[bold]{Markup.Escape(_label)}[/]")];
        if (!string.IsNullOrEmpty(_description))
        {
            lines.Add(Rail.Of($"[grey]{Markup.Escape(_description)}[/]"));
        }

        lines.Add(Rail.Of(_input.Length > 0 || string.IsNullOrEmpty(_defaultValue)
            ? $"{Markup.Escape(_input.ToString())}[grey]{_caret}[/]"
            : $"[grey]{_caret}{Markup.Escape(_defaultValue)}[/]"));
        if (_error is not null)
        {
            lines.Add(Rail.Of($"[yellow]{Markup.Escape(_error)}[/]"));
        }

        lines.Add(Rail.Line(Rail.End, colour, string.Empty));

        return new Rows(lines);
    }
}

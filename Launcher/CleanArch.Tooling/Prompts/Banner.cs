using System.Text;
using Spectre.Console;

namespace CleanArch.Tooling.Prompts;

public static class Banner
{
    public const string Name = "CleanArch";
    public const string Tool = "CleanArch.Create";
    public const string Tagline = "Clean Architecture for .NET 10, minus the setup.";

    private static readonly string[] _letters =
    [
        " ██████╗██╗     ███████╗ █████╗ ███╗   ██╗ █████╗ ██████╗  ██████╗██╗  ██╗",
        "██╔════╝██║     ██╔════╝██╔══██╗████╗  ██║██╔══██╗██╔══██╗██╔════╝██║  ██║",
        "██║     ██║     █████╗  ███████║██╔██╗ ██║███████║██████╔╝██║     ███████║",
        "██║     ██║     ██╔══╝  ██╔══██║██║╚██╗██║██╔══██║██╔══██╗██║     ██╔══██║",
        "╚██████╗███████╗███████╗██║  ██║██║ ╚████║██║  ██║██║  ██║╚██████╗██║  ██║",
        " ╚═════╝╚══════╝╚══════╝╚═╝  ╚═╝╚═╝  ╚═══╝╚═╝  ╚═╝╚═╝  ╚═╝ ╚═════╝╚═╝  ╚═╝",
    ];

    private static readonly Color _from = new(0, 190, 255);
    private static readonly Color _to = new(170, 90, 255);
    private static readonly Color _shadow = new(60, 60, 90);
    private static readonly Color _frame = new(95, 95, 175);
    private static readonly Padding _inside = new(2, 1, 2, 1);

    public static int Width { get; } = _letters.Max(line => line.Length) + _inside.Left + _inside.Right + 2;

    public static void Write(IAnsiConsole console, string? version)
    {
        string versioned = version is null ? Tool : $"{Tool} {version}";
        if (console.Profile.Width < Width)
        {
            console.MarkupLine($"[bold {_from.ToMarkup()}]{Name}[/]  [grey]{Markup.Escape(versioned)}[/]");
            console.MarkupLine($"[bold]{Markup.Escape(Tagline)}[/]");
            console.WriteLine();
            return;
        }

        string body = string.Join('\n', [.. _letters.Select(Painted), string.Empty, $"[bold]{Markup.Escape(Tagline)}[/]"]);
        console.Write(new Panel(new Markup(body))
            .Border(BoxBorder.Square)
            .BorderColor(_frame)
            .Header($"[grey] {Markup.Escape(versioned)} [/]", Justify.Right)
            .Padding(_inside));
        console.WriteLine();
    }

    private static string Painted(string line)
    {
        int width = _letters.Max(letter => letter.Length);
        var painted = new StringBuilder();
        for (int column = 0; column < line.Length; column++)
        {
            char glyph = line[column];
            Color colour = glyph == '█' ? Mixed(column / (double)width) : _shadow;
            painted.Append(glyph == ' ' ? " " : $"[{colour.ToMarkup()}]{glyph}[/]");
        }

        return painted.ToString();
    }

    private static Color Mixed(double at) =>
        new(
            (byte)(_from.R + ((_to.R - _from.R) * at)),
            (byte)(_from.G + ((_to.G - _from.G) * at)),
            (byte)(_from.B + ((_to.B - _from.B) * at)));
}

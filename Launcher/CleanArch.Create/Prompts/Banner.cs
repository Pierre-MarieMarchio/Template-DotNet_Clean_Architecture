using Spectre.Console;
using Spectre.Console.Rendering;

namespace CleanArch.Create.Prompts;

public static class Banner
{
    public const string Name = "CleanArch";
    public const string Tool = "CleanArch.Create";
    public const string Tagline = ".NET 10 Clean Architecture API";

    public static void Write(IAnsiConsole console, string? version)
    {
        IRenderable figlet = new FigletText(Name).Color(Color.DeepSkyBlue1);
        int width = Segment.SplitLines(figlet.Render(RenderOptions.Create(console), int.MaxValue))
            .Max(line => line.Sum(segment => segment.CellCount()));
        if (width < console.Profile.Width)
        {
            console.Write(figlet);
        }

        string versioned = version is null ? string.Empty : " " + Markup.Escape(version);
        console.MarkupLine($"[bold deepskyblue1]{Tool}[/][grey]{versioned} · {Tagline}[/]");
        console.WriteLine();
    }
}

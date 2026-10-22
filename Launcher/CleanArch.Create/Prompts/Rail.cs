using Spectre.Console;
using Spectre.Console.Rendering;

namespace CleanArch.Create.Prompts;

public sealed class Rail : IRenderable
{
    public const string Bar = "│";
    public const string Start = "┌";
    public const string End = "└";
    public const string Asking = "◆";
    public const string Answered = "◇";
    public const string Warning = "▲";
    public const string Note = "●";
    public const string Gutter = "  ";

    private static readonly Style _barStyle = new(Color.Grey);
    private static readonly int _width = Bar.Length + Gutter.Length;

    private readonly IRenderable _content;

    public Rail(IRenderable content)
    {
        _content = content;
    }

    public static Rail Of(string markup) => new(new Markup(markup));

    public static Markup Line(string glyph, string colour, string markup) =>
        new($"[{colour}]{glyph}[/]{Gutter}{markup}");

    public static Markup Empty() => new($"[grey]{Bar}[/]");

    public Measurement Measure(RenderOptions options, int maxWidth)
    {
        Measurement content = _content.Measure(options, Math.Max(1, maxWidth - _width));
        return new Measurement(content.Min + _width, content.Max + _width);
    }

    public IEnumerable<Segment> Render(RenderOptions options, int maxWidth)
    {
        foreach (SegmentLine line in Segment.SplitLines(_content.Render(options, Math.Max(1, maxWidth - _width))))
        {
            yield return new Segment(Bar, _barStyle);
            yield return new Segment(Gutter);
            foreach (Segment segment in line)
            {
                yield return segment;
            }

            yield return Segment.LineBreak;
        }
    }
}

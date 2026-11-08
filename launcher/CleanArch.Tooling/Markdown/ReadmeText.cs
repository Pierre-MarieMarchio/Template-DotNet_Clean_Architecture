using System.Text.RegularExpressions;

namespace CleanArch.Tooling.Markdown;

public static partial class ReadmeText
{
    private const string _columnGap = "  ";

    public static IEnumerable<(string Text, bool Code)> Blocks(string markdown)
    {
        var reader = new BlockReader();
        foreach (string line in markdown.Split('\n').Select(line => line.TrimEnd('\r')))
        {
            foreach ((string Text, bool Code) block in reader.Read(line))
            {
                yield return block;
            }
        }

        foreach ((string Text, bool Code) block in reader.End())
        {
            yield return block;
        }
    }

    public static string Plain(string markdown) => string.Join('\n', Blocks(markdown).Select(block => block.Text));

    private static string Inline(string text) => Autolink().Replace(Link().Replace(text.Replace("`", string.Empty, StringComparison.Ordinal), "$1 ($2)"), "$1");

    private static IEnumerable<string> Aligned(List<string> rows)
    {
        List<string[]> cells = [.. rows
            .Where(row => !Separator().IsMatch(row))
            .Select(row => row.Trim().Trim('|').Split('|').Select(cell => Inline(cell.Trim())).ToArray())];
        if (cells.Count == 0)
        {
            return [];
        }

        int[] widths = [.. Enumerable.Range(0, cells.Max(row => row.Length))
            .Select(column => cells.Max(row => column < row.Length ? row[column].Length : 0))];

        return cells.Select(row => string.Join(_columnGap, row.Select((cell, column) => cell.PadRight(widths[column]))).TrimEnd());
    }

    [GeneratedRegex(@"\[([^\]]+)\]\(([^)]+)\)")]
    private static partial Regex Link();

    [GeneratedRegex(@"<(https?://[^>]+)>")]
    private static partial Regex Autolink();

    [GeneratedRegex(@"^\|[\s:|-]+\|$")]
    private static partial Regex Separator();

    private sealed class BlockReader
    {
        private readonly List<string> _table = [];
        private bool _code;
        private string? _paragraph;

        public List<(string Text, bool Code)> Read(string line)
        {
            if (!_code && line.StartsWith('|'))
            {
                _table.Add(line);
                return [];
            }

            List<(string Text, bool Code)> blocks = Table();
            bool fence = MarkdownFence.Opens(line);
            if (!_code && !fence && line.Length > 0)
            {
                _paragraph = _paragraph is null ? Inline(line) : _paragraph + " " + Inline(line);
                return blocks;
            }

            Paragraph(blocks);
            if (fence)
            {
                _code = !_code;
            }
            else
            {
                blocks.Add((line, _code));
            }

            return blocks;
        }

        public List<(string Text, bool Code)> End()
        {
            List<(string Text, bool Code)> blocks = Table();
            Paragraph(blocks);
            return blocks;
        }

        private List<(string Text, bool Code)> Table()
        {
            List<(string Text, bool Code)> blocks = [.. Aligned(_table).Select(aligned => (aligned, false))];
            _table.Clear();
            return blocks;
        }

        private void Paragraph(List<(string Text, bool Code)> blocks)
        {
            if (_paragraph is not null)
            {
                blocks.Add((_paragraph, false));
                _paragraph = null;
            }
        }
    }
}

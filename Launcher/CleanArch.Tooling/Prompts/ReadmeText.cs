using System.Text.RegularExpressions;

namespace CleanArch.Tooling.Prompts;

public static partial class ReadmeText
{
    private const string _fence = "```";
    private const string _columnGap = "  ";

    public static IEnumerable<(string Text, bool Code)> Blocks(string markdown)
    {
        bool code = false;
        string? paragraph = null;
        var table = new List<string>();
        foreach (string line in markdown.Split('\n').Select(line => line.TrimEnd('\r')))
        {
            bool fence = line.TrimStart().StartsWith(_fence, StringComparison.Ordinal);
            bool row = !code && line.StartsWith('|');
            if (row)
            {
                table.Add(line);
                continue;
            }

            foreach (string aligned in Aligned(table))
            {
                yield return (aligned, false);
            }

            table.Clear();
            if (!code && !fence && line.Length > 0)
            {
                paragraph = paragraph is null ? Inline(line) : paragraph + " " + Inline(line);
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

        foreach (string aligned in Aligned(table))
        {
            yield return (aligned, false);
        }

        if (paragraph is not null)
        {
            yield return (paragraph, false);
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
}

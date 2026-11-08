using System.Text;
using System.Text.RegularExpressions;
using CleanArch.Tooling.Additions;
using CleanArch.Tooling.Markdown;

namespace CleanArch.Cli.Joins;

internal static partial class ReadmeEdits
{
    public static bool Lists(string readme) => MarkdownSection.Of(readme, ItemAdditions.JoiningSection) is not null;

    public static List<ReadmeEdit> Listed(string root, string relative)
    {
        string section = MarkdownSection.Of(Path.Combine(root, relative), ItemAdditions.JoiningSection)
            ?? throw new JoinRefusedException($"{relative} has no '{ItemAdditions.JoiningSection}' section, so there is nothing to join.");

        return Parse(section.ReplaceLineEndings("\n").Split('\n'), relative);
    }

    public static List<ReadmeEdit> Parse(string[] section, string relative)
    {
        var edits = new List<ReadmeEdit>();

        for (int index = 0; index < section.Length; index++)
        {
            if (section[index].StartsWith("- in ", StringComparison.Ordinal))
            {
                edits.Add(Edit(section, ref index, relative, edits.Count + 1));
            }
        }

        return edits.Count > 0
            ? edits
            : throw new JoinRefusedException($"{relative}: '{ItemAdditions.JoiningSection}' lists no edit in the form this task reads.");
    }

    private static ReadmeEdit Edit(string[] section, ref int index, string relative, int number)
    {
        string header = Header(section, ref index);
        Match bullet = Bullet().Match(header);
        if (!bullet.Success)
        {
            throw new JoinRefusedException($"{relative}: '{header}' names no file and no 'after', 'before', 'replace' or 'remove'.");
        }

        string mode = bullet.Groups["mode"].Value;
        List<string> anchor = Fence(section, ref index, relative);
        List<string> lines = mode == "remove" ? [] : Fence(section, ref index, relative);
        return new ReadmeEdit(relative, number, bullet.Groups["path"].Value, mode, anchor, lines);
    }

    private static string Header(string[] section, ref int index)
    {
        var header = new StringBuilder(section[index].Trim());
        while (header[^1] != ':' && ++index < section.Length)
        {
            header.Append(' ').Append(section[index].Trim());
        }

        return header.ToString();
    }

    private static List<string> Fence(string[] section, ref int index, string relative)
    {
        while (++index < section.Length && !MarkdownFence.Opens(section[index]))
        {
            if (section[index].StartsWith("- ", StringComparison.Ordinal) || section[index].StartsWith("## ", StringComparison.Ordinal))
            {
                throw new JoinRefusedException($"{relative}: an edit ends at '{section[index]}' before its lines.");
            }
        }

        if (index >= section.Length)
        {
            throw new JoinRefusedException($"{relative} ends inside an edit.");
        }

        string fence = MarkdownFence.Marker(section[index]);
        int indent = section[index].Length - section[index].TrimStart().Length;
        var lines = new List<string>();

        while (++index < section.Length && !MarkdownFence.Closes(section[index], fence))
        {
            lines.Add(Unindented(section[index], indent));
        }

        return lines;
    }

    private static string Unindented(string line, int indent) =>
        line.Length >= indent && line[..indent].Trim().Length == 0 ? line[indent..] : line.TrimStart();

    [GeneratedRegex(@"^- in `(?<path>[^`]+)`, (?<mode>after|before|replace|remove) [^:]*:$", RegexOptions.None, matchTimeoutMilliseconds: 2000)]
    private static partial Regex Bullet();
}

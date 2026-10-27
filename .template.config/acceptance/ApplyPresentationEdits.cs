#:property UseAppHost=false

using System.Text;
using System.Text.RegularExpressions;

return PresentationEdits.Run(args);

internal static class PresentationEdits
{
    private const string Usage =
        """
        Usage: dotnet run .template.config/acceptance/ApplyPresentationEdits.cs <presentation README.md> <root>

        Makes the edits the presentation project's README lists under its section for a project
        generated without one, in the generated project at <root>: each block of lines added after or
        before its anchor, or put in its place, or the anchor removed, the anchor found exactly once.
        It reads the README and nothing else, so an edit the README no longer gives in that form fails
        here before it misleads a reader.

        Exit code 0 once every edit is applied; 1 on any problem, after writing nothing.
        """;

    private const string Section = "## Joining a project generated without one";

    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(2);

    private static readonly Regex Bullet = new(
        @"^- in `(?<path>[^`]+)`, (?<mode>after|before|replace|remove) [^:]*:$", RegexOptions.None, Timeout);

    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    private sealed record Edit(string Path, string Mode, List<string> Anchor, List<string> Lines);

    internal static int Run(string[] arguments)
    {
        if (arguments is not [string readme, string root] || arguments.Contains("--help"))
        {
            Console.WriteLine(Usage);
            return arguments.Contains("--help") ? 0 : 1;
        }

        try
        {
            List<Edit> edits = Edits(File.ReadAllLines(readme));
            var files = new Dictionary<string, List<string>>(StringComparer.Ordinal);

            foreach (Edit edit in edits)
            {
                if (!files.TryGetValue(edit.Path, out List<string>? lines))
                {
                    lines = [.. File.ReadAllText(Path.Combine(root, edit.Path)).Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n')];
                    files[edit.Path] = lines;
                }

                Apply(edit, lines);
                Console.WriteLine($"Applied: {edit.Mode} {edit.Anchor.Count} line(s) of {edit.Path}");
            }

            foreach ((string path, List<string> lines) in files)
            {
                string full = Path.Combine(root, path);
                bool bom = File.ReadAllBytes(full) is [0xEF, 0xBB, 0xBF, ..];
                File.WriteAllText(full, string.Join('\n', lines), bom ? new UTF8Encoding(true) : Utf8);
            }

            return 0;
        }
        catch (Exception error) when (error is InvalidOperationException or IOException)
        {
            Console.Error.WriteLine(error.Message);
            return 1;
        }
    }

    private static List<Edit> Edits(string[] readme)
    {
        int start = Array.IndexOf(readme, Section);

        if (start < 0)
        {
            throw new InvalidOperationException($"The README has no '{Section}' section.");
        }

        var edits = new List<Edit>();
        int index = start + 1;

        while (index < readme.Length && !readme[index].StartsWith("## ", StringComparison.Ordinal))
        {
            if (!readme[index].StartsWith("- in ", StringComparison.Ordinal))
            {
                index++;
                continue;
            }

            string header = readme[index].Trim();
            while (!header.EndsWith(':') && ++index < readme.Length)
            {
                header += " " + readme[index].Trim();
            }

            Match bullet = Bullet.Match(header);
            if (!bullet.Success)
            {
                throw new InvalidOperationException($"'{header}' names no file and no 'after', 'before', 'replace' or 'remove'.");
            }

            string mode = bullet.Groups["mode"].Value;
            List<string> anchor = Fence(readme, ref index);
            List<string> lines = mode == "remove" ? [] : Fence(readme, ref index);
            edits.Add(new Edit(bullet.Groups["path"].Value, mode, anchor, lines));
        }

        if (edits.Count == 0)
        {
            throw new InvalidOperationException($"'{Section}' lists no edit in the form this reads.");
        }

        return edits;
    }

    private static List<string> Fence(string[] readme, ref int index)
    {
        while (++index < readme.Length && !readme[index].TrimStart().StartsWith("```", StringComparison.Ordinal))
        {
            if (readme[index].StartsWith("- ", StringComparison.Ordinal) || readme[index].StartsWith("## ", StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"An edit ends at '{readme[index]}' before its lines.");
            }
        }

        if (index >= readme.Length)
        {
            throw new InvalidOperationException("The README ends inside an edit.");
        }

        int indent = readme[index].Length - readme[index].TrimStart().Length;
        var lines = new List<string>();

        while (++index < readme.Length && readme[index].Trim() != "```")
        {
            string line = readme[index];
            lines.Add(line.Length >= indent && line[..indent].Trim().Length == 0 ? line[indent..] : line.TrimStart());
        }

        return lines;
    }

    private static void Apply(Edit edit, List<string> lines)
    {
        int[] found = [.. Enumerable.Range(0, Math.Max(0, lines.Count - edit.Anchor.Count + 1))
            .Where(at => edit.Anchor.Select((line, offset) => lines[at + offset].TrimEnd() == line.TrimEnd()).All(same => same))];

        if (found.Length != 1)
        {
            throw new InvalidOperationException(
                $"{edit.Path}: the lines to place the edit by occur {found.Length} times, not once:\n{string.Join('\n', edit.Anchor)}");
        }

        int at = found[0];

        switch (edit.Mode)
        {
            case "after":
                lines.InsertRange(at + edit.Anchor.Count, edit.Lines);
                break;
            case "before":
                lines.InsertRange(at, edit.Lines);
                break;
            default:
                lines.RemoveRange(at, edit.Anchor.Count);
                lines.InsertRange(at, edit.Lines);
                break;
        }
    }
}

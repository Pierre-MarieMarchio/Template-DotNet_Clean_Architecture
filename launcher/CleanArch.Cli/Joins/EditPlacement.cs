namespace CleanArch.Cli.Joins;

internal sealed record PlacedEdits(IReadOnlyList<ReadmeEdit> Made, IReadOnlyList<ReadmeEdit> InPlace);

internal static class EditPlacement
{
    public static Dictionary<string, List<string>> Read(string root, IEnumerable<ReadmeEdit> edits)
    {
        var files = new Dictionary<string, List<string>>(StringComparer.Ordinal);

        foreach (ReadmeEdit edit in edits.Where(edit => !files.ContainsKey(edit.Path)))
        {
            string full = Path.Combine(root, edit.Path);
            if (!File.Exists(full))
            {
                throw new JoinRefusedException($"{edit}: {edit.Path} does not exist, so no file was changed.");
            }

            files[edit.Path] = [.. File.ReadAllText(full).Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n')];
        }

        return files;
    }

    public static PlacedEdits Make(IEnumerable<ReadmeEdit> edits, Dictionary<string, List<string>> files)
    {
        var made = new List<ReadmeEdit>();
        var inPlace = new List<ReadmeEdit>();

        foreach (ReadmeEdit edit in edits)
        {
            if (InPlace(edit, files[edit.Path]))
            {
                inPlace.Add(edit);
            }
            else
            {
                Apply(edit, files[edit.Path]);
                made.Add(edit);
            }
        }

        return new PlacedEdits(made, inPlace);
    }

    private static int[] Occurrences(List<string> lines, IReadOnlyList<string> block) =>
        [.. Enumerable.Range(0, Math.Max(0, lines.Count - block.Count + 1)).Where(at => Holds(lines, block, at))];

    private static bool Holds(List<string> lines, IReadOnlyList<string> block, int at) =>
        at >= 0 && at + block.Count <= lines.Count && block.Select((line, offset) => lines[at + offset].TrimEnd() == line.TrimEnd()).All(same => same);

    private static bool InPlace(ReadmeEdit edit, List<string> lines)
    {
        int[] anchors = Occurrences(lines, edit.Anchor);

        return edit.Mode switch
        {
            "after" => anchors.Any(at => Holds(lines, edit.Lines, at + edit.Anchor.Count)),
            "before" => anchors.Any(at => Holds(lines, edit.Lines, at - edit.Lines.Count)),
            "replace" => Occurrences(lines, edit.Lines) is { Length: > 0 } placed
                && anchors.All(at => placed.Any(start => at >= start && at + edit.Anchor.Count <= start + edit.Lines.Count)),
            _ => anchors.Length == 0,
        };
    }

    private static void Apply(ReadmeEdit edit, List<string> lines)
    {
        int[] found = Occurrences(lines, edit.Anchor);
        if (found.Length != 1)
        {
            throw new JoinRefusedException(
                $"{edit}: the lines it is placed by occur {found.Length} times in {edit.Path}, not once, so no file was changed:\n{string.Join('\n', edit.Anchor)}");
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

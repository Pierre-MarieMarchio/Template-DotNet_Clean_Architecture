using System.Text;
using CleanArch.Cli.Generators.Shared;

namespace CleanArch.Cli.Generators.RemoveFeature;

internal static class RemovalUndos
{
    internal static Dictionary<string, HashSet<string>> ForeignLines(string root, string features)
    {
        var foreign = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        string folder = Path.Combine(root, FeatureManifest.Folder);

        foreach (string file in Directory.Exists(folder) ? Directory.EnumerateFiles(folder, "*.json") : [])
        {
            string other = Path.GetFileNameWithoutExtension(file);

            if (other == features || FeatureManifest.Read(root, other, out _) is not { } manifest)
            {
                continue;
            }

            foreach (var edit in manifest.Edits)
            {
                if (!foreign.TryGetValue(edit.Path, out var lines))
                {
                    foreign[edit.Path] = lines = new HashSet<string>(StringComparer.Ordinal);
                }

                lines.UnionWith(edit.Lines);
            }
        }

        return foreign;
    }

    internal static List<Undo> PlanUndos(
        string root,
        IReadOnlyList<TextEdits.Edit> edits,
        Dictionary<string, HashSet<string>> foreign,
        Dictionary<string, (byte[] Original, byte[] After)> undone,
        Dictionary<string, HashSet<int>> dropped)
    {
        var states = new Dictionary<TextEdits.Edit, UndoState>(ReferenceEqualityComparer.Instance);

        foreach (var group in edits.GroupBy(edit => edit.Path, StringComparer.Ordinal))
        {
            string full = Path.Combine(root, group.Key);

            if (!File.Exists(full))
            {
                foreach (var edit in group)
                {
                    states[edit] = UndoState.Gone;
                }

                continue;
            }

            byte[] original = File.ReadAllBytes(full);
            bool bom = original is [0xEF, 0xBB, 0xBF, ..];
            string text = Encoding.UTF8.GetString(original, bom ? 3 : 0, original.Length - (bom ? 3 : 0));
            string newline = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
            bool finalNewline = text.EndsWith('\n');
            List<string> lines = [.. (finalNewline ? text[..^newline.Length] : text).Split(newline)];
            List<int> origin = [.. Enumerable.Range(0, lines.Count)];
            int count = lines.Count;
            var twins = group.CountBy(Twin, StringComparer.Ordinal).ToDictionary(StringComparer.Ordinal);

            foreach (var edit in group.Reverse())
            {
                var (state, positions, at) = Locate(lines, edit, twins[Twin(edit)]--, foreign.GetValueOrDefault(group.Key) ?? []);

                states[edit] = state;

                if (state != UndoState.Undo)
                {
                    continue;
                }

                foreach (int position in Enumerable.Reverse(positions))
                {
                    lines.RemoveAt(position);
                    origin.RemoveAt(position);
                }

                lines.InsertRange(at, edit.Replaced);
                origin.InsertRange(at, edit.Replaced.Select(_ => -1));
            }

            byte[] after = [.. bom ? Encoding.UTF8.Preamble : [], .. Encoding.UTF8.GetBytes(string.Join(newline, lines) + (finalNewline ? newline : string.Empty))];

            if (!after.AsSpan().SequenceEqual(original))
            {
                undone[group.Key] = (original, after);
                dropped[group.Key] = [.. Enumerable.Range(0, count).Except(origin)];
            }
        }

        return [.. edits.Select(edit => new Undo(edit, states[edit]))];
    }

    internal static string Twin(TextEdits.Edit edit) =>
        string.Join('\u0001', [edit.Anchor, edit.Position, .. edit.Lines, "\u0002", .. edit.Replaced]);

    internal static (UndoState State, int[] Positions, int At) Locate(List<string> lines, TextEdits.Edit edit, int twins, HashSet<string> foreign)
    {
        string[] added = edit.Lines;
        bool before = edit.Position == "before";
        int[] anchors = [.. Enumerable.Range(0, lines.Count).Where(index => lines[index] == edit.Anchor)];

        bool Holds(int at, string[] block) =>
            at >= 0 && at + block.Length <= lines.Count && lines.Skip(at).Take(block.Length).SequenceEqual(block, StringComparer.Ordinal);

        int[] Beside(string[] block) => [.. anchors.Select(index => before ? index - block.Length : index + 1).Where(at => Holds(at, block))];

        int[] Span(int at) => [.. Enumerable.Range(at, added.Length)];

        if (added.Length == 0)
        {
            return Beside(edit.Replaced).Length > 0 ? (UndoState.Gone, [], -1)
                : anchors.Length == 1 || anchors.Length == twins ? (UndoState.Undo, [], before ? anchors[0] : anchors[0] + 1)
                : (UndoState.Displaced, [], -1);
        }

        int[] beside = Beside(added);

        if (beside.Length == 1 || (beside.Length > 0 && beside.Length == twins))
        {
            return (UndoState.Undo, Span(beside[0]), beside[0]);
        }

        string[] visible = [.. added.Where(line => line.Trim().Length > 0)];

        if (beside.Length > twins || visible.Length == 0)
        {
            return (beside.Length > twins ? UndoState.Displaced : UndoState.Gone, [], -1);
        }

        int[]? Spread(int start)
        {
            var positions = new List<int> { start };

            for (int next = start + 1, index = 1; index < added.Length; next++)
            {
                if (next >= lines.Count)
                {
                    return null;
                }

                if (lines[next] == added[index])
                {
                    positions.Add(next);
                    index++;
                }
                else if (!foreign.Contains(lines[next]))
                {
                    return null;
                }
            }

            return [.. positions];
        }

        int[] contiguous = [.. Enumerable.Range(0, lines.Count).Where(at => Holds(at, added))];

        if (contiguous.Length == 1 || (contiguous.Length > 0 && contiguous.Length == twins))
        {
            return (UndoState.Undo, Span(contiguous[0]), contiguous[0]);
        }

        int[][] spread = [.. Enumerable.Range(0, lines.Count).Where(at => lines[at] == added[0]).Select(Spread).OfType<int[]>()];

        if (contiguous.Length == 0 && (spread.Length == 1 || (spread.Length > 0 && spread.Length == twins)))
        {
            return (UndoState.Undo, spread[0], spread[0][0]);
        }

        return contiguous.Length + spread.Length == 0 && !visible.Any(lines.Contains) ? (UndoState.Gone, [], -1) : (UndoState.Displaced, [], -1);
    }
}

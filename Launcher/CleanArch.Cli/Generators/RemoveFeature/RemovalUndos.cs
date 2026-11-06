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

            if (File.Exists(full))
            {
                UndoIn(group, File.ReadAllBytes(full), foreign.GetValueOrDefault(group.Key) ?? [], states, undone, dropped);
            }
            else
            {
                foreach (var edit in group)
                {
                    states[edit] = UndoState.Gone;
                }
            }
        }

        return [.. edits.Select(edit => new Undo(edit, states[edit]))];
    }

    private static void UndoIn(
        IGrouping<string, TextEdits.Edit> group,
        byte[] original,
        HashSet<string> foreign,
        Dictionary<TextEdits.Edit, UndoState> states,
        Dictionary<string, (byte[] Original, byte[] After)> undone,
        Dictionary<string, HashSet<int>> dropped)
    {
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
            var (state, positions, at) = new EditLocator(lines, edit, twins[Twin(edit)]--, foreign).Locate();

            states[edit] = state;

            if (state == UndoState.Undo)
            {
                Apply(lines, origin, edit, positions, at);
            }
        }

        byte[] after = [.. bom ? Encoding.UTF8.Preamble : [], .. Encoding.UTF8.GetBytes(string.Join(newline, lines) + (finalNewline ? newline : string.Empty))];

        if (!after.AsSpan().SequenceEqual(original))
        {
            undone[group.Key] = (original, after);
            dropped[group.Key] = [.. Enumerable.Range(0, count).Except(origin)];
        }
    }

    private static void Apply(List<string> lines, List<int> origin, TextEdits.Edit edit, int[] positions, int at)
    {
        foreach (int position in Enumerable.Reverse(positions))
        {
            lines.RemoveAt(position);
            origin.RemoveAt(position);
        }

        lines.InsertRange(at, edit.Replaced);
        origin.InsertRange(at, edit.Replaced.Select(_ => -1));
    }

    internal static string Twin(TextEdits.Edit edit) =>
        string.Join('\u0001', [edit.Anchor, edit.Position, .. edit.Lines, "\u0002", .. edit.Replaced]);

    private sealed class EditLocator(List<string> lines, TextEdits.Edit edit, int twins, HashSet<string> foreign)
    {
        private static readonly (UndoState State, int[] Positions, int At) _gone = (UndoState.Gone, [], -1);

        private static readonly (UndoState State, int[] Positions, int At) _displaced = (UndoState.Displaced, [], -1);

        private readonly string[] _added = edit.Lines;

        private readonly bool _before = edit.Position == "before";

        private readonly int[] _anchors = [.. Enumerable.Range(0, lines.Count).Where(index => lines[index] == edit.Anchor)];

        internal (UndoState State, int[] Positions, int At) Locate()
        {
            if (_added.Length == 0)
            {
                return LocateReplaced();
            }

            int[] beside = Beside(_added);

            if (IsUnique(beside.Length))
            {
                return (UndoState.Undo, Span(beside[0]), beside[0]);
            }

            if (beside.Length > twins)
            {
                return _displaced;
            }

            string[] visible = [.. _added.Where(line => line.Trim().Length > 0)];

            return visible.Length == 0 ? _gone : LocateElsewhere(visible);
        }

        private (UndoState State, int[] Positions, int At) LocateReplaced()
        {
            if (Beside(edit.Replaced).Length > 0)
            {
                return _gone;
            }

            if (_anchors.Length != 1 && _anchors.Length != twins)
            {
                return _displaced;
            }

            return (UndoState.Undo, [], _before ? _anchors[0] : _anchors[0] + 1);
        }

        private (UndoState State, int[] Positions, int At) LocateElsewhere(string[] visible)
        {
            int[] contiguous = [.. Enumerable.Range(0, lines.Count).Where(at => Holds(at, _added))];

            if (IsUnique(contiguous.Length))
            {
                return (UndoState.Undo, Span(contiguous[0]), contiguous[0]);
            }

            int[][] spread = [.. Enumerable.Range(0, lines.Count).Where(at => lines[at] == _added[0]).Select(Spread).OfType<int[]>()];

            if (contiguous.Length == 0 && IsUnique(spread.Length))
            {
                return (UndoState.Undo, spread[0], spread[0][0]);
            }

            return contiguous.Length + spread.Length == 0 && !visible.Any(lines.Contains) ? _gone : _displaced;
        }

        private bool IsUnique(int found) => found == 1 || (found > 0 && found == twins);

        private bool Holds(int at, string[] block) =>
            at >= 0 && at + block.Length <= lines.Count && lines.Skip(at).Take(block.Length).SequenceEqual(block, StringComparer.Ordinal);

        private int[] Beside(string[] block) => [.. _anchors.Select(index => _before ? index - block.Length : index + 1).Where(at => Holds(at, block))];

        private int[] Span(int at) => [.. Enumerable.Range(at, _added.Length)];

        private int[]? Spread(int start)
        {
            var positions = new List<int> { start };
            int next = start;
            int index = 1;

            while (index < _added.Length)
            {
                next++;

                if (next >= lines.Count)
                {
                    return null;
                }

                if (lines[next] == _added[index])
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
    }
}

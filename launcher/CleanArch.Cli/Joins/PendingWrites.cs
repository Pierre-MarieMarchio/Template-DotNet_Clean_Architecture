using CleanArch.Tooling.Additions;

namespace CleanArch.Cli.Joins;

internal sealed class PendingWrites(string root)
{
    private readonly Dictionary<string, (string Text, bool Bom)> _files = new(StringComparer.Ordinal);

    public IReadOnlyCollection<string> Paths => _files.Keys;

    public void Edited(IEnumerable<ReadmeEdit> made, Dictionary<string, List<string>> files)
    {
        foreach (string path in made.Select(edit => edit.Path).Distinct(StringComparer.Ordinal))
        {
            _files[path] = (string.Join('\n', files[path]), ProjectTree.HasBom(Path.Combine(root, path)));
        }
    }

    public void Moved(IEnumerable<FileMove> moves, Func<string, string> renamed)
    {
        foreach (FileMove move in moves)
        {
            string from = Path.Combine(root, move.From);
            _files[move.To] = (renamed(File.ReadAllText(from)), ProjectTree.HasBom(from));
        }
    }

    public void Renamed(IEnumerable<string> paths, Func<string, string> renamed)
    {
        foreach (string path in paths)
        {
            (string text, bool bom) = Current(path);
            string changed = renamed(text);

            if (changed != text || _files.ContainsKey(path))
            {
                _files[path] = (changed, bom);
            }
        }
    }

    public void WithoutTheJoiningSection(string readme)
    {
        string[] lines = Current(readme).Text.Split('\n');
        int start = Array.FindIndex(lines, line => line.TrimEnd('\r') == ItemAdditions.JoiningSection);
        int next = Array.FindIndex(lines, start + 1, line => line.StartsWith("## ", StringComparison.Ordinal));

        string text = next < 0
            ? string.Join('\n', lines[..start]).TrimEnd() + "\n"
            : string.Join('\n', [.. lines[..start], .. lines[next..]]);
        _files[readme] = (text, ProjectTree.HasBom(Path.Combine(root, readme)));
    }

    public void WriteTo(string target, IReadOnlyCollection<string> leaving)
    {
        foreach ((string path, (string text, bool bom)) in _files)
        {
            ProjectTree.Write(Path.Combine(target, path), text, bom);
        }

        foreach (string path in leaving)
        {
            File.Delete(Path.Combine(target, path));
        }

        ProjectTree.RemoveEmptyFolders(leaving.Select(path => Path.GetDirectoryName(Path.Combine(target, path))!));
    }

    private (string Text, bool Bom) Current(string path) =>
        _files.TryGetValue(path, out var pending)
            ? pending
            : (File.ReadAllText(Path.Combine(root, path)), ProjectTree.HasBom(Path.Combine(root, path)));
}

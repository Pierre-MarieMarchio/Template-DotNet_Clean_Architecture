namespace CleanArch.Tooling.Markdown;

public static class MarkdownFence
{
    private const char _tick = '`';
    private const int _minimum = 3;

    public static bool Opens(string line) => Marker(line).Length > 0;

    public static string Marker(string line)
    {
        string trimmed = line.TrimStart();
        int length = trimmed.Length - trimmed.TrimStart(_tick).Length;
        return length >= _minimum ? new string(_tick, length) : string.Empty;
    }

    public static bool Closes(string line, string marker)
    {
        string trimmed = line.Trim();
        return trimmed.Length >= marker.Length && trimmed.All(character => character == _tick);
    }
}

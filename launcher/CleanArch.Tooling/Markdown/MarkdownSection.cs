namespace CleanArch.Tooling.Markdown;

public static class MarkdownSection
{
    public static string? Of(string path, string heading)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        string[] lines = File.ReadAllLines(path);
        int start = Array.IndexOf(lines, heading);
        if (start < 0)
        {
            return null;
        }

        int level = HeadingDepth(heading);
        var body = new List<string>();
        string fence = string.Empty;
        foreach (string line in lines.Skip(start + 1))
        {
            if (fence.Length == 0 && MarkdownFence.Opens(line))
            {
                fence = MarkdownFence.Marker(line);
            }
            else if (fence.Length > 0 && MarkdownFence.Closes(line, fence))
            {
                fence = string.Empty;
            }
            else if (fence.Length == 0 && HeadingDepth(line) is > 0 and int depth && depth <= level)
            {
                break;
            }

            body.Add(line);
        }

        return string.Join(Environment.NewLine, body).Trim();
    }

    private static int HeadingDepth(string line)
    {
        int depth = line.Length - line.TrimStart('#').Length;
        return depth > 0 && line.Length > depth && line[depth] == ' ' ? depth : 0;
    }
}

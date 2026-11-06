namespace CleanArch.Tooling.Markdown;

internal static class MarkdownFence
{
    public static bool Opens(string line) => line.TrimStart().StartsWith("```", StringComparison.Ordinal);
}

using System.Text.RegularExpressions;

namespace CleanArch.Tooling;

internal static partial class ProjectNames
{
    public static string FolderOf(string name, string part) => $"{string.Concat(name.Split('.').Select(SegmentInitials))}.{part}";

    public static string KebabName(string name) => Separators().Replace(name.ToLowerInvariant(), "-");

    private static string SegmentInitials(string segment) =>
        segment.Any(char.IsAsciiLetterUpper)
            ? string.Concat(segment.Where(char.IsAsciiLetterUpper))
            : string.Concat(segment.Where(char.IsAsciiLetter).Take(1).Select(char.ToUpperInvariant));

    [GeneratedRegex(@"[.\s_]+")]
    private static partial Regex Separators();
}

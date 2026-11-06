using System.Text;
using System.Text.RegularExpressions;

namespace CleanArch.Cli.UpgradeReport;

internal static class Changelog
{
    private static readonly Regex _heading = new(@"^## \[?(?<version>\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?)\]?", RegexOptions.None, TimeSpan.FromSeconds(2));

    public static string Between(string changelog, string from, string to)
    {
        var kept = new StringBuilder();
        bool keeping = false;

        foreach (string line in changelog.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            if (line.StartsWith("## ", StringComparison.Ordinal))
            {
                keeping = WithinTheRange(line, from, to);
            }

            if (keeping)
            {
                kept.Append(line).Append('\n');
            }
        }

        return kept.Length > 0 ? kept.ToString().TrimEnd() : $"CHANGELOG.md holds no entry after {from} up to {to}.";
    }

    public static string Of(TemplatePackage newer, string from, string to) =>
        newer.Changelog is { } text
            ? Between(text, from, to)
            : $"Version {to} of the package carries no CHANGELOG.md; the repository it names holds the entries: {newer.Repository ?? "(the package names none)"}.";

    private static bool WithinTheRange(string line, string from, string to)
    {
        Match heading = _heading.Match(line);

        return heading.Success
            && SemanticVersion.Compare(heading.Groups["version"].Value, from) > 0
            && SemanticVersion.Compare(heading.Groups["version"].Value, to) <= 0;
    }
}

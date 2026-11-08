using System.Globalization;
using System.Text.RegularExpressions;

namespace CleanArch.Cli.UpgradeReport;

internal static class SemanticVersion
{
    private static readonly Regex _version = new(
        @"^(?<core>\d+\.\d+\.\d+)(?:-(?<pre>[0-9A-Za-z.-]+))?(?:\+[0-9A-Za-z.-]+)?$", RegexOptions.None, TimeSpan.FromSeconds(2));

    public static bool IsVersion(string version) => Parse(version) is not null;

    public static bool IsStable(string version) => Parse(version) is { Prerelease: null };

    public static readonly Comparer<string> Order = Comparer<string>.Create(Compare);

    public static int Compare(string? left, string? right)
    {
        (int[] leftCore, string[]? leftPre) = Parse(left ?? string.Empty) ?? ([0, 0, 0], null);
        (int[] rightCore, string[]? rightPre) = Parse(right ?? string.Empty) ?? ([0, 0, 0], null);

        int core = leftCore.Zip(rightCore, (one, other) => one.CompareTo(other)).FirstOrDefault(order => order != 0);
        if (core != 0)
        {
            return core;
        }

        if (leftPre is null || rightPre is null)
        {
            return (leftPre is null ? 1 : 0) - (rightPre is null ? 1 : 0);
        }

        int identifiers = leftPre.Zip(rightPre, CompareIdentifiers).FirstOrDefault(order => order != 0);
        return identifiers != 0 ? identifiers : leftPre.Length.CompareTo(rightPre.Length);
    }

    private static (int[] Core, string[]? Prerelease)? Parse(string version)
    {
        Match match = _version.Match(version.Trim());
        if (!match.Success)
        {
            return null;
        }

        int[] core = [.. match.Groups["core"].Value.Split('.').Select(part => int.Parse(part, CultureInfo.InvariantCulture))];
        return (core, match.Groups["pre"].Success ? match.Groups["pre"].Value.Split('.') : null);
    }

    private static int CompareIdentifiers(string left, string right)
    {
        bool leftNumber = int.TryParse(left, NumberStyles.None, CultureInfo.InvariantCulture, out int leftValue);
        bool rightNumber = int.TryParse(right, NumberStyles.None, CultureInfo.InvariantCulture, out int rightValue);

        return (leftNumber, rightNumber) switch
        {
            (true, true) => leftValue.CompareTo(rightValue),
            (true, false) => -1,
            (false, true) => 1,
            _ => string.CompareOrdinal(left.ToLowerInvariant(), right.ToLowerInvariant()),
        };
    }
}

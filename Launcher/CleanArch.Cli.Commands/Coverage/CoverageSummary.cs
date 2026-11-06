using System.Globalization;

namespace CleanArch.Cli.Commands.Coverage;

internal static class CoverageSummary
{
    public static string Render(IReadOnlyList<AssemblyCoverage> assemblies, CoverageTotal total, int reportCount, double minimum)
    {
        List<string> table =
        [
            $"### Coverage — {Fixed2(total.LineRate)}% of lines (floor {General(minimum)}%)",
            "",
            $"Merged from {reportCount} report(s) as a union over (source file, line).",
            "",
            "| Assembly | Lines | Line rate | Branches | Branch rate |",
            "|---|---:|---:|---:|---:|",
            .. assemblies.Select(row =>
                $"| `{row.Assembly}` | {row.LinesCovered}/{row.LinesValid} | {Fixed2(row.LineRate)}% | {row.BranchesCovered}/{row.BranchesValid} | {Fixed2(row.BranchRate)}% |"),
            $"| **Total** | **{total.LinesCovered}/{total.LinesValid}** | **{Fixed2(total.LineRate)}%** | **{total.BranchesCovered}/{total.BranchesValid}** | **{Fixed2(total.BranchRate)}%** |",
        ];
        return string.Join("\n", table) + "\n";
    }

    public static string Fixed2(double value) => value.ToString("F2", CultureInfo.InvariantCulture);

    public static string General(double value) =>
        value.ToString("G6", CultureInfo.InvariantCulture).Replace("E", "e", StringComparison.Ordinal);
}

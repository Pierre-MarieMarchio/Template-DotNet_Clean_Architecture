using System.Globalization;
using System.Text;

namespace CleanArch.Cli.Commands.Coverage;

internal static class CoverageJson
{
    public static string Render(IReadOnlyList<AssemblyCoverage> assemblies, CoverageTotal total, IReadOnlyList<string> reports, double minimum)
    {
        var builder = new StringBuilder("{\n");
        builder.Append($"  \"minimum\": {Number(minimum)},\n");
        builder.Append($"  \"lineRate\": {Number(Math.Round(total.LineRate, 4, MidpointRounding.ToEven))},\n");
        builder.Append($"  \"branchRate\": {Number(Math.Round(total.BranchRate, 4, MidpointRounding.ToEven))},\n");
        builder.Append($"  \"linesCovered\": {total.LinesCovered},\n");
        builder.Append($"  \"linesValid\": {total.LinesValid},\n");
        builder.Append("  \"reports\": ").Append(List(reports.Select(report => "    " + Quote(report)), "  ")).Append(",\n");
        builder.Append("  \"assemblies\": ").Append(List(assemblies.Select(Entry), "  ")).Append('\n');
        return builder.Append("}\n").ToString();
    }

    private static string List(IEnumerable<string> items, string indent)
    {
        string[] lines = [.. items];
        return lines.Length == 0 ? "[]" : "[\n" + string.Join(",\n", lines) + "\n" + indent + "]";
    }

    private static string Entry(AssemblyCoverage row) =>
        "    {\n"
        + $"      \"assembly\": {Quote(row.Assembly)},\n"
        + $"      \"linesCovered\": {row.LinesCovered},\n"
        + $"      \"linesValid\": {row.LinesValid},\n"
        + $"      \"lineRate\": {Number(row.LineRate)},\n"
        + $"      \"branchesCovered\": {row.BranchesCovered},\n"
        + $"      \"branchesValid\": {row.BranchesValid},\n"
        + $"      \"branchRate\": {Number(row.BranchRate)}\n"
        + "    }";

    private static string Number(double value)
    {
        string text = value.ToString("R", CultureInfo.InvariantCulture);
        return text.Contains('.', StringComparison.Ordinal) || text.Contains('E', StringComparison.Ordinal)
            ? text.Replace("E", "e", StringComparison.Ordinal)
            : text + ".0";
    }

    private static string Quote(string value)
    {
        var builder = new StringBuilder("\"");
        foreach (char character in value)
        {
            builder.Append(Escaped(character));
        }

        return builder.Append('"').ToString();
    }

    private static string Escaped(char character) =>
        character switch
        {
            '"' => "\\\"",
            '\\' => "\\\\",
            '\n' => "\\n",
            '\r' => "\\r",
            '\t' => "\\t",
            < ' ' => $"\\u{(int)character:x4}",
            _ => character.ToString(),
        };
}

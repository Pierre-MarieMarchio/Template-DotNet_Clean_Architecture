using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace CleanArch.Cli.Commands.Coverage;

internal static partial class CoberturaReports
{
    public static IReadOnlyList<string> Find(string root) =>
        Directory.Exists(root)
            ? [.. Directory.EnumerateFiles(root, "*.cobertura.xml", SearchOption.AllDirectories).Order(StringComparer.Ordinal)]
            : [];

    public static IReadOnlyList<AssemblyCoverage> Merge(IEnumerable<string> reports)
    {
        var merged = new SortedDictionary<string, AssemblyLines>(StringComparer.Ordinal);
        foreach (string report in reports)
        {
            foreach (XElement package in XDocument.Load(report).Descendants("package"))
            {
                string assembly = package.Attribute("name")?.Value ?? "(unnamed)";
                if (!merged.TryGetValue(assembly, out AssemblyLines? lines))
                {
                    lines = new AssemblyLines();
                    merged[assembly] = lines;
                }

                AddPackage(package, lines);
            }
        }

        return [.. merged.Select(entry => entry.Value.Measure(entry.Key))];
    }

    private static void AddPackage(XElement package, AssemblyLines lines)
    {
        foreach (XElement type in package.Descendants("class"))
        {
            string file = type.Attribute("filename")?.Value ?? "";
            foreach (XElement line in type.Descendants("line"))
            {
                AddLine(file, line, lines);
            }
        }
    }

    private static void AddLine(string file, XElement line, AssemblyLines lines)
    {
        int number = Number(line.Attribute("number")?.Value);
        lines.RecordLine(file, number, Number(line.Attribute("hits")?.Value));
        if (line.Attribute("branch")?.Value == "true" && Conditions().Match(line.Attribute("condition-coverage")?.Value ?? "") is { Success: true } match)
        {
            lines.RecordBranches(file, number, Number(match.Groups[1].Value), Number(match.Groups[2].Value));
        }
    }

    private static int Number(string? value) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed) ? parsed : 0;

    [GeneratedRegex(@"\((\d+)/(\d+)\)")]
    private static partial Regex Conditions();
}

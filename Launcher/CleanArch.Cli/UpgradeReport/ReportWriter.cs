using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace CleanArch.Cli.UpgradeReport;

internal static class ReportWriter
{
    private static readonly JsonSerializerOptions _written = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        NewLine = "\n",
    };

    public static void Print(Report report, TextWriter output)
    {
        Comparison comparison = report.Comparison;
        void Section(string title, IReadOnlyCollection<string> lines)
        {
            output.WriteLine();
            output.WriteLine($"{title} ({lines.Count})");

            foreach (string line in lines)
            {
                output.WriteLine("  " + line);
            }
        }

        string[] Of(FindingKind kind) => [.. comparison.Findings.Where(finding => finding.Kind == kind).Select(finding => finding.Note is null ? finding.Path : $"{finding.Path}   ({finding.Note})")];

        output.WriteLine();
        output.WriteLine($"Upgrade report for {report.Name}: from {report.From} to {report.To}. Nothing was written to this project.");
        Section("Decisions to take by hand", [.. report.Decisions, .. comparison.Provenance is null ? Array.Empty<string>() : [comparison.Provenance]]);
        Section($"Take as {report.To} writes it: {report.To} changed these and the project did not", Of(FindingKind.Take));
        Section($"Merge by hand: {report.To} changed these and so did the project; the diffs from {report.From} close this report", Of(FindingKind.Merge));
        Section($"Added by {report.To}", Of(FindingKind.Added));
        Section($"Removed by {report.To}", Of(FindingKind.Removed));
        Section("Changed scaffold descriptors", comparison.Scaffolds);
        Section(
            "From bootstrap or a framework's CLI, not compared",
            [.. comparison.Bootstrap.Select(group => $"{group.Key}: {group.Value.Count} file(s), such as {group.Value[0]}")]);
        PrintClosing(report, output);
    }

    public static void WriteJson(Report report, string file)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, ToJson(report).ToJsonString(_written) + "\n");
    }

    private static void PrintClosing(Report report, TextWriter output)
    {
        output.WriteLine();
        output.WriteLine($"The project's own files, in neither generation and not concerned: {report.Comparison.Own.Count}{(report.Json is null ? string.Empty : $", listed in {report.Json}")}.");
        output.WriteLine($"Files {report.To} left unchanged, or that need nothing: {report.Comparison.Unchanged}.");
        output.WriteLine(report.Kept is null
            ? "--keep <folder> keeps the two generations outside the project, so a file to take can be copied from there."
            : $"The two generations are kept in {report.Kept}: take a file from {Path.Combine(report.Kept, report.To)}, "
                + $"and merge one three ways with git merge-file <path> {Path.Combine(report.Kept, report.From)}/<path> {Path.Combine(report.Kept, report.To)}/<path>.");
        output.WriteLine("After taking files, run `dotnet run Tools/Tasks.cs format-fix`: it orders the using directives as bootstrap did.");
        output.WriteLine();
        output.WriteLine($"Changelog after {report.From} up to {report.To}");
        output.WriteLine(report.Changelog);

        if (report.Diffs.Count == 0)
        {
            return;
        }

        output.WriteLine();
        output.WriteLine($"The diffs from {report.From} to {report.To} of the files to merge by hand");

        foreach (string diff in report.Diffs)
        {
            output.WriteLine();
            output.WriteLine(diff);
        }
    }

    private static JsonObject ToJson(Report report)
    {
        JsonArray Paths(FindingKind kind) => [.. report.Comparison.Findings.Where(finding => finding.Kind == kind)
            .Select(finding => (JsonNode)new JsonObject { ["path"] = finding.Path, ["note"] = finding.Note })];
        static JsonArray Texts(IEnumerable<string> texts) => new([.. texts.Select(text => (JsonNode)JsonValue.Create(text))]);

        return new JsonObject
        {
            ["name"] = report.Name,
            ["from"] = report.From,
            ["to"] = report.To,
            ["kept"] = report.Kept,
            ["decisions"] = Texts(report.Decisions),
            ["provenance"] = report.Comparison.Provenance,
            ["take"] = Paths(FindingKind.Take),
            ["merge"] = Paths(FindingKind.Merge),
            ["added"] = Paths(FindingKind.Added),
            ["removed"] = Paths(FindingKind.Removed),
            ["scaffolds"] = Texts(report.Comparison.Scaffolds),
            ["bootstrap"] = new JsonObject(report.Comparison.Bootstrap.Select(group => KeyValuePair.Create(group.Key, (JsonNode?)Texts(group.Value)))),
            ["own"] = Texts(report.Comparison.Own),
            ["unchanged"] = report.Comparison.Unchanged,
            ["diffs"] = Texts(report.Diffs),
            ["changelog"] = report.Changelog,
        };
    }
}

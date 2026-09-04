#:property UseAppHost=false

// Merge the Cobertura reports the coverage extension writes, then enforce a line-coverage floor.
//
// Why this exists rather than a `dotnet test` flag: one report is written per test module,
// and the same product assembly is exercised by more than one of them. Summing the
// `lines-valid` / `lines-covered` attributes across those files double-counts every shared
// line and produces a number that is wrong in whichever direction the overlap happens to
// lean. The correct merge is a union over (source file, line): a line is covered if any
// suite covered it. That is what this script does, with the base class library only, so it
// runs identically on a developer's Windows box and on an ubuntu runner.
//
// Usage:
//     dotnet run Tools/CoverageGate.cs --minimum 62 [--root TestResults] [--summary out.md] [--json out.json]
//     dotnet run Tools/CoverageGate.cs --self-test
//
// Exit status: 0 when total line coverage >= --minimum, 1 when below it, 2 on bad input
// (no reports found, unparseable XML, or reports that between them measure no line at all).
//
// The last of those is the subtle one. A rate is a quotient, and a quotient over zero has to
// be given a value: per assembly, 100% is the right one, because an assembly with nothing
// measurable in it should not drag a total down. Applied to the *total*, that same convention
// turns "nothing was measured" into "everything is covered" — the exact false green a report
// missing an assembly produces, and the one `coverage.minimum` names as the cost this
// repository has already paid. So the per-assembly convention stays and the total refuses to
// use it.

using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

if (args.Contains("--self-test"))
{
    return CoverageGate.SelfTest();
}

return CoverageGate.Run(args);

/// <summary>The merge, the floor, and the fixtures that prove the gate can fail.</summary>
internal static class CoverageGate
{
    private const string ProgramName = "CoverageGate.cs";

    private const string Usage =
        "usage: CoverageGate.cs [-h] [--root ROOT] --minimum MINIMUM\n"
        + "                       [--summary SUMMARY] [--json JSON_OUT] [--self-test]";

    private static readonly Regex Condition = new(@"\((\d+)/(\d+)\)", RegexOptions.Compiled);

    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    public static List<string> IterReports(string root)
    {
        if (!Directory.Exists(root))
        {
            return [];
        }

        // A pattern rather than a fixed name, because the writer names the file and two writers
        // name it differently: one report per module under a GUID of its own
        // ('<guid>.cobertura.xml'), or a fixed name inside a directory of its own
        // ('<guid>/coverage.cobertura.xml'). Matching the extension covers both, and the union
        // below is indifferent to how many files it reads or what they are called.
        return Directory
            .EnumerateFiles(root, "*.cobertura.xml", SearchOption.AllDirectories)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>assembly -&gt; lines keyed by (file, line), branches keyed the same way.</summary>
    public static SortedDictionary<string, Bucket> Merge(List<string> reports)
    {
        var merged = new SortedDictionary<string, Bucket>(StringComparer.Ordinal);

        foreach (var report in reports)
        {
            var document = XDocument.Load(report);

            foreach (var package in document.Descendants("package"))
            {
                var assembly = package.Attribute("name")?.Value ?? "(unnamed)";
                if (!merged.TryGetValue(assembly, out var bucket))
                {
                    bucket = new Bucket();
                    merged[assembly] = bucket;
                }

                foreach (var element in package.Descendants("class"))
                {
                    var filename = element.Attribute("filename")?.Value ?? "";

                    foreach (var line in element.Descendants("line"))
                    {
                        var number = Number(line.Attribute("number")?.Value);
                        var hits = Number(line.Attribute("hits")?.Value);
                        var key = (filename, number);

                        // Union: covered by any suite counts as covered.
                        bucket.Lines[key] = bucket.Lines.TryGetValue(key, out var previousHits)
                            ? Math.Max(previousHits, hits)
                            : hits;

                        if (line.Attribute("branch")?.Value == "true")
                        {
                            var match = Condition.Match(line.Attribute("condition-coverage")?.Value ?? "");
                            if (match.Success)
                            {
                                var covered = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
                                var total = int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
                                var previous = bucket.Branches.TryGetValue(key, out var stored)
                                    ? stored
                                    : (Covered: 0, Total: total);
                                bucket.Branches[key] = (Math.Max(previous.Covered, covered), total);
                            }
                        }
                    }
                }
            }
        }

        return merged;
    }

    public static (int LinesCovered, int LinesValid, int BranchesCovered, int BranchesValid) Rates(Bucket bucket)
        => (
            bucket.Lines.Values.Count(hits => hits > 0),
            bucket.Lines.Count,
            bucket.Branches.Values.Sum(branch => branch.Covered),
            bucket.Branches.Values.Sum(branch => branch.Total));

    /// <summary>
    /// The per-assembly convention: an assembly with no measurable line is not a shortfall.
    /// Legitimate for a row, wrong for the total — see the header. The total's guard lives in
    /// <see cref="Gate"/>, which refuses to divide rather than calling this with a zero.
    /// </summary>
    public static double Percent(int covered, int total)
        => total != 0 ? 100.0 * covered / total : 100.0;

    public static int Gate(string root, double minimum, string? summary = null, string? jsonOut = null)
    {
        var reports = IterReports(root);

        if (reports.Count == 0)
        {
            Console.Error.WriteLine(
                $"::error title=No coverage report::No *.cobertura.xml under '{root}'.");
            return 2;
        }

        SortedDictionary<string, Bucket> merged;
        try
        {
            merged = Merge(reports);
        }
        catch (XmlException error)
        {
            Console.Error.WriteLine($"::error title=Unreadable coverage report::{error.Message}");
            return 2;
        }

        var rows = new List<Row>();
        foreach (var (assembly, bucket) in merged)
        {
            var (linesCovered, linesTotal, branchesCovered, branchesTotal) = Rates(bucket);
            rows.Add(new Row(
                assembly,
                linesCovered,
                linesTotal,
                Percent(linesCovered, linesTotal),
                branchesCovered,
                branchesTotal,
                Percent(branchesCovered, branchesTotal)));
        }

        var totalLinesCovered = rows.Sum(row => row.LinesCovered);
        var totalLinesValid = rows.Sum(row => row.LinesValid);
        var totalBranchesCovered = rows.Sum(row => row.BranchesCovered);
        var totalBranchesValid = rows.Sum(row => row.BranchesValid);

        // No percentage is printed here on purpose: the only one available would be the empty
        // quotient's 100%, and reporting it is how a gate that measured nothing looks like a pass.
        if (totalLinesValid == 0)
        {
            Console.Error.WriteLine(
                $"::error title=Coverage report measures nothing::{reports.Count} report(s) under "
                + $"'{root}' hold {rows.Count} assembly(ies) and not one measurable line. That is a "
                + "broken report, not full coverage: check that the collector ran and that every "
                + "product assembly is still in it.");
            return 2;
        }

        var lineRate = Percent(totalLinesCovered, totalLinesValid);
        var branchRate = Percent(totalBranchesCovered, totalBranchesValid);

        var table = new List<string>
        {
            $"### Coverage — {Fixed2(lineRate)}% of lines (floor {General(minimum)}%)",
            "",
            $"Merged from {reports.Count} report(s) as a union over (source file, line).",
            "",
            "| Assembly | Lines | Line rate | Branches | Branch rate |",
            "|---|---:|---:|---:|---:|",
        };

        foreach (var row in rows)
        {
            table.Add(
                $"| `{row.Assembly}` | {row.LinesCovered}/{row.LinesValid} | {Fixed2(row.LineRate)}% "
                + $"| {row.BranchesCovered}/{row.BranchesValid} | {Fixed2(row.BranchRate)}% |");
        }

        table.Add(
            $"| **Total** | **{totalLinesCovered}/{totalLinesValid}** | **{Fixed2(lineRate)}%** "
            + $"| **{totalBranchesCovered}/{totalBranchesValid}** | **{Fixed2(branchRate)}%** |");

        var rendered = string.Join("\n", table) + "\n";

        foreach (var line in table)
        {
            Console.WriteLine(line);
        }

        Console.WriteLine();

        foreach (var destination in new[] { summary, Environment.GetEnvironmentVariable("GITHUB_STEP_SUMMARY") })
        {
            if (!string.IsNullOrEmpty(destination))
            {
                File.AppendAllText(destination, rendered, Utf8NoBom);
            }
        }

        if (!string.IsNullOrEmpty(jsonOut))
        {
            File.WriteAllText(jsonOut, RenderJson(minimum, lineRate, branchRate, totalLinesCovered, totalLinesValid, reports, rows), Utf8NoBom);
        }

        if (lineRate + 1e-9 < minimum)
        {
            Console.Error.WriteLine(
                $"::error title=Coverage below floor::{Fixed2(lineRate)}% of lines covered, "
                + $"floor is {General(minimum)}%. Cover the new code or argue the floor down in a "
                + "separate commit.");
            return 1;
        }

        Console.WriteLine($"Coverage {Fixed2(lineRate)}% >= floor {General(minimum)}%.");
        return 0;
    }

    public static int Run(string[] arguments)
    {
        var root = "TestResults";
        double? minimum = null;
        string? summary = null;
        string? jsonOut = null;

        for (var index = 0; index < arguments.Length; index++)
        {
            var argument = arguments[index];

            if (argument is "-h" or "--help")
            {
                PrintHelp();
                return 0;
            }

            if (argument is "--self-test")
            {
                continue;
            }

            var separator = argument.IndexOf('=', StringComparison.Ordinal);
            var option = separator >= 0 ? argument[..separator] : argument;
            var inline = separator >= 0 ? argument[(separator + 1)..] : null;

            if (option is not ("--root" or "--minimum" or "--summary" or "--json"))
            {
                return UsageError($"unrecognized arguments: {argument}");
            }

            string value;
            if (inline is not null)
            {
                value = inline;
            }
            else if (index + 1 < arguments.Length)
            {
                value = arguments[++index];
            }
            else
            {
                return UsageError($"argument {option}: expected one argument");
            }

            switch (option)
            {
                case "--root":
                    root = value;
                    break;
                case "--minimum":
                    if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
                    {
                        return UsageError($"argument --minimum: invalid float value: '{value}'");
                    }

                    minimum = parsed;
                    break;
                case "--summary":
                    summary = value;
                    break;
                default:
                    jsonOut = value;
                    break;
            }
        }

        if (minimum is null)
        {
            return UsageError("the following arguments are required: --minimum");
        }

        return Gate(root, minimum.Value, summary, jsonOut);
    }

    private static void PrintHelp()
    {
        Console.WriteLine(Usage);
        Console.WriteLine();
        Console.WriteLine("options:");
        Console.WriteLine("  -h, --help         show this help message and exit");
        Console.WriteLine("  --root ROOT        directory searched recursively");
        Console.WriteLine("  --minimum MINIMUM  line-coverage floor, in percent");
        Console.WriteLine("  --summary SUMMARY  write a Markdown table here (append)");
        Console.WriteLine("  --json JSON_OUT    write the merged totals here");
        Console.WriteLine("  --self-test        prove the gate can fail");
    }

    // --- Self-test -------------------------------------------------------------------------------
    //
    // The two halves a gate needs in order to be worth its place in CI: proof that it fails on the
    // reports it must reject, and proof that it passes on the ones it must accept. The pair that
    // matters most is the last of each list — a valid report with no line in it must be rejected,
    // while an empty assembly standing beside a measured one must not be, because the convention that
    // is wrong for the total is right for the row.

    private static string Report(params string[] packages)
        => "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<coverage><packages>\n"
            + string.Join("\n", packages)
            + "\n</packages></coverage>\n";

    private static string Package(string name, string filename, params (int Number, int Hits)[] lines)
    {
        var rendered = string.Concat(
            lines.Select(line => $"<line number=\"{line.Number}\" hits=\"{line.Hits}\" />"));
        return $"<package name=\"{name}\"><classes>"
            + $"<class name=\"{name}.Type\" filename=\"{filename}\"><lines>{rendered}</lines></class>"
            + "</classes></package>";
    }

    private const string EmptyPackage = "<package name=\"App.Ghost\"><classes></classes></package>";

    private static (string Label, (string Path, string Content)[] Files, double Minimum, int Expected)[] MustFail()
    =>
    [
        ("no report at all", [], 85.0, 2),
        (
            "a valid report holding no package",
            [("a/coverage.cobertura.xml", Report())],
            85.0,
            2
        ),
        (
            "a report whose only assembly has no measurable line",
            [("a/coverage.cobertura.xml", Report(EmptyPackage))],
            85.0,
            2
        ),
        (
            "XML that does not parse",
            [("a/coverage.cobertura.xml", "<coverage><packages>")],
            85.0,
            2
        ),
        (
            "coverage genuinely below the floor",
            [("a/coverage.cobertura.xml", Report(Package("App.Domain", "A.cs", (1, 1), (2, 0))))],
            85.0,
            1
        ),
    ];

    private static (string Label, (string Path, string Content)[] Files, double Minimum)[] MustPass()
    =>
    [
        (
            "coverage above the floor",
            [("a/coverage.cobertura.xml", Report(Package("App.Domain", "A.cs", (1, 1), (2, 1))))],
            85.0
        ),
        (
            "coverage exactly on the floor",
            [("a/coverage.cobertura.xml", Report(Package("App.Domain", "A.cs", (1, 1), (2, 0))))],
            50.0
        ),
        (
            "two suites covering the same line, merged as a union rather than summed",
            [
                ("a/coverage.cobertura.xml", Report(Package("App.Domain", "A.cs", (1, 0), (2, 1)))),
                ("b/coverage.cobertura.xml", Report(Package("App.Domain", "A.cs", (1, 1), (2, 0)))),
            ],
            100.0
        ),
        (
            "an assembly with no measurable line standing beside one that has some",
            [
                (
                    "a/coverage.cobertura.xml",
                    Report(Package("App.Domain", "A.cs", (1, 1), (2, 1)), EmptyPackage)
                ),
            ],
            85.0
        ),
    ];

    private static void Materialise(string root, (string Path, string Content)[] files)
    {
        foreach (var (relative, content) in files)
        {
            var path = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content, Utf8NoBom);
        }
    }

    /// <summary>
    /// The fixtures' own output is swallowed, stderr included: a `::error` annotation written by a
    /// fixture that failed on purpose would be shown by GitHub against a job that passed.
    /// </summary>
    private static int SilentGate(string root, double minimum)
    {
        var outWriter = Console.Out;
        var errorWriter = Console.Error;
        try
        {
            Console.SetOut(new StringWriter());
            Console.SetError(new StringWriter());
            return Gate(root, minimum);
        }
        finally
        {
            Console.SetOut(outWriter);
            Console.SetError(errorWriter);
        }
    }

    public static int SelfTest()
    {
        var failures = new List<string>();
        var mustFail = MustFail();
        var mustPass = MustPass();

        // A fixture must not append its table to the real job summary.
        var summary = Environment.GetEnvironmentVariable("GITHUB_STEP_SUMMARY");
        Environment.SetEnvironmentVariable("GITHUB_STEP_SUMMARY", null);

        try
        {
            foreach (var (label, files, minimum, expected) in mustFail)
            {
                WithTemporaryTree(files, root =>
                {
                    var status = SilentGate(root, minimum);
                    if (status != expected)
                    {
                        failures.Add($"expected exit {expected}, got {status}, on: {label}");
                    }
                });
            }

            foreach (var (label, files, minimum) in mustPass)
            {
                WithTemporaryTree(files, root =>
                {
                    var status = SilentGate(root, minimum);
                    if (status != 0)
                    {
                        failures.Add($"expected exit 0, got {status}, on: {label}");
                    }
                });
            }

            // The union is a claim about arithmetic, not about exit codes: two reports naming the same
            // two lines must yield two lines, not four.
            WithTemporaryTree(mustPass[2].Files, root =>
            {
                var (covered, total, _, _) = Rates(Merge(IterReports(root))["App.Domain"]);
                if (covered != 2 || total != 2)
                {
                    failures.Add($"the union of two reports over two lines gave {covered}/{total}");
                }
            });

            // The convention the total refuses is still the right one for a row.
            if (Percent(0, 0) != 100.0)
            {
                failures.Add("an assembly with no measurable line should rate 100%, not a division");
            }
        }
        finally
        {
            if (summary is not null)
            {
                Environment.SetEnvironmentVariable("GITHUB_STEP_SUMMARY", summary);
            }
        }

        var totalClaims = mustFail.Length + mustPass.Length + 2;
        Console.WriteLine(
            $"Self-test: {mustFail.Length} report(s) that must fail the gate, "
            + $"{mustPass.Length} that must pass it, 2 arithmetic claim(s).");
        Console.WriteLine();

        if (failures.Count > 0)
        {
            Console.WriteLine($"SELF-TEST FAILURES ({failures.Count} of {totalClaims}):");
            foreach (var failure in failures)
            {
                Console.WriteLine($"  ! {failure}");
            }

            return 1;
        }

        Console.WriteLine("The gate rejects every report that measures nothing and accepts every one that does.");
        return 0;
    }

    private static void WithTemporaryTree((string Path, string Content)[] files, Action<string> body)
    {
        var root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            Materialise(root, files);
            body(root);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    // --- Plumbing --------------------------------------------------------------------------------

    private static int Number(string? value)
        => string.IsNullOrEmpty(value) ? 0
            : int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed
            : 0;

    private static string Fixed2(double value) => value.ToString("F2", CultureInfo.InvariantCulture);

    private static string General(double value)
        => value.ToString("G6", CultureInfo.InvariantCulture).Replace("E", "e", StringComparison.Ordinal);

    private static int UsageError(string message)
    {
        Console.Error.WriteLine(Usage);
        Console.Error.WriteLine($"{ProgramName}: error: {message}");
        return 2;
    }

    private static string RenderJson(
        double minimum,
        double lineRate,
        double branchRate,
        int linesCovered,
        int linesValid,
        List<string> reports,
        List<Row> rows)
    {
        var builder = new StringBuilder();
        builder.Append("{\n");
        builder.Append($"  \"minimum\": {Repr(minimum)},\n");
        builder.Append($"  \"lineRate\": {Repr(Math.Round(lineRate, 4, MidpointRounding.ToEven))},\n");
        builder.Append($"  \"branchRate\": {Repr(Math.Round(branchRate, 4, MidpointRounding.ToEven))},\n");
        builder.Append($"  \"linesCovered\": {linesCovered},\n");
        builder.Append($"  \"linesValid\": {linesValid},\n");
        builder.Append("  \"reports\": " + (reports.Count == 0 ? "[]" : "[\n"));
        for (var index = 0; index < reports.Count; index++)
        {
            builder.Append($"    {Quote(reports[index])}{(index + 1 < reports.Count ? "," : "")}\n");
        }

        builder.Append(reports.Count == 0 ? ",\n" : "  ],\n");
        builder.Append("  \"assemblies\": " + (rows.Count == 0 ? "[]\n" : "[\n"));
        for (var index = 0; index < rows.Count; index++)
        {
            var row = rows[index];
            builder.Append("    {\n");
            builder.Append($"      \"assembly\": {Quote(row.Assembly)},\n");
            builder.Append($"      \"linesCovered\": {row.LinesCovered},\n");
            builder.Append($"      \"linesValid\": {row.LinesValid},\n");
            builder.Append($"      \"lineRate\": {Repr(row.LineRate)},\n");
            builder.Append($"      \"branchesCovered\": {row.BranchesCovered},\n");
            builder.Append($"      \"branchesValid\": {row.BranchesValid},\n");
            builder.Append($"      \"branchRate\": {Repr(row.BranchRate)}\n");
            builder.Append($"    }}{(index + 1 < rows.Count ? "," : "")}\n");
        }

        if (rows.Count > 0)
        {
            builder.Append("  ]\n");
        }

        builder.Append("}\n");
        return builder.ToString();
    }

    /// <summary>A double rendered the way a JSON encoder renders one: shortest round trip, always fractional.</summary>
    private static string Repr(double value)
    {
        var text = value.ToString("R", CultureInfo.InvariantCulture);
        return text.Contains('.', StringComparison.Ordinal) || text.Contains('E', StringComparison.Ordinal)
            ? text.Replace("E", "e", StringComparison.Ordinal)
            : text + ".0";
    }

    private static string Quote(string value)
    {
        var builder = new StringBuilder("\"");
        foreach (var character in value)
        {
            builder.Append(character switch
            {
                '"' => "\\\"",
                '\\' => "\\\\",
                '\n' => "\\n",
                '\r' => "\\r",
                '\t' => "\\t",
                < ' ' => $"\\u{(int)character:x4}",
                _ => character.ToString(),
            });
        }

        return builder.Append('"').ToString();
    }
}

/// <summary>One assembly's merged lines and branches, keyed by (source file, line number).</summary>
internal sealed class Bucket
{
    public Dictionary<(string File, int Line), int> Lines { get; } = [];

    public Dictionary<(string File, int Line), (int Covered, int Total)> Branches { get; } = [];
}

/// <summary>One row of the summary table.</summary>
internal sealed record Row(
    string Assembly,
    int LinesCovered,
    int LinesValid,
    double LineRate,
    int BranchesCovered,
    int BranchesValid,
    double BranchRate);

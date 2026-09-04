#:property UseAppHost=false

// Sums the counters of every TRX the test run produced, writes them as a Markdown table for the
// job summary, and refuses a run that executed nothing.
//
// That last part is the reason this is a gate rather than a report. `dotnet test` over a solution
// exits zero when it finds no tests to run, so a project missing from the solution, a filter that
// matches nothing, or a build that produced no test assembly all read as success. The count is the
// only thing that tells those apart from a run that passed.
//
// TRX rather than the console output: the console interleaves when assemblies run in parallel, and
// a line can be spliced from two of them. The counters element is written once per file, after the
// run, by the process that owns it.
//
//     dotnet run Tools/TestSummary.cs --root TestResults --summary "$GITHUB_STEP_SUMMARY"
//     dotnet run Tools/TestSummary.cs --self-test

using System.Xml.Linq;

const string TrxNamespace = "http://microsoft.com/schemas/VisualStudio/TeamTest/2010";

if (args.Contains("--self-test"))
{
    return SelfTest();
}

string root = ValueOf("--root") ?? "TestResults";
string? summaryPath = ValueOf("--summary");

var totals = Sum(root, out int files);

Console.WriteLine($"Read {files} TRX file(s) under '{root}'.");

string table = string.Join(
    Environment.NewLine,
    "### Test results",
    "",
    "| Total | Passed | Failed | Skipped |",
    "|---:|---:|---:|---:|",
    $"| {totals.Total} | {totals.Passed} | {totals.Failed} | {totals.Skipped} |");

Console.WriteLine();
Console.WriteLine(table);

if (summaryPath is not null)
{
    File.AppendAllText(summaryPath, table + Environment.NewLine);
}

if (totals.Total == 0)
{
    Console.Error.WriteLine(
        "::error title=Zero tests executed::The run produced no test results. A green run with no "
        + "tests is a false pass: dotnet test exits zero when it finds nothing to run, so a project "
        + "missing from the solution reads exactly like a project whose tests all passed.");

    return 1;
}

return 0;

string? ValueOf(string name)
{
    int index = Array.IndexOf(args, name);

    return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
}

static Counters Sum(string root, out int files)
{
    files = 0;

    if (!Directory.Exists(root))
    {
        return default;
    }

    var totals = default(Counters);

    foreach (string path in Directory.EnumerateFiles(root, "*.trx", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
    {
        var counters = Read(path);
        if (counters is null)
        {
            // A TRX without a counters element is a run that did not finish writing one. Counted as
            // read so that the file count stays honest, and contributing nothing to the totals.
            files++;
            continue;
        }

        files++;
        totals = totals.Add(counters.Value);
    }

    return totals;
}

static Counters? Read(string path)
{
    XDocument document;

    try
    {
        document = XDocument.Load(path);
    }
    catch (System.Xml.XmlException)
    {
        return null;
    }

    XNamespace ns = TrxNamespace;
    var element = document.Root?.Element(ns + "ResultSummary")?.Element(ns + "Counters");

    if (element is null)
    {
        return null;
    }

    return new Counters(
        Attribute(element, "total"),
        Attribute(element, "passed"),
        Attribute(element, "failed"),
        Attribute(element, "notExecuted"));

    static int Attribute(XElement element, string name) =>
        int.TryParse(element.Attribute(name)?.Value, out int value) ? value : 0;
}

static int SelfTest()
{
    // The halves a gate needs to be worth its place: proof that it counts what a real run writes,
    // and proof that it refuses a run that executed nothing rather than reporting four zeroes and
    // exiting clean.
    string root = Path.Combine(Path.GetTempPath(), $"trx-self-test-{Guid.NewGuid():N}");
    var failures = new List<string>();

    try
    {
        Directory.CreateDirectory(Path.Combine(root, "one"));
        Directory.CreateDirectory(Path.Combine(root, "two"));

        File.WriteAllText(Path.Combine(root, "one", "a.trx"), Trx(total: 10, passed: 7, failed: 2, notExecuted: 1));
        File.WriteAllText(Path.Combine(root, "two", "b.trx"), Trx(total: 5, passed: 5, failed: 0, notExecuted: 0));
        File.WriteAllText(Path.Combine(root, "two", "unfinished.trx"), "<TestRun />");

        var summed = Sum(root, out int files);

        if (files != 3)
        {
            failures.Add($"three TRX files were written and {files} were read");
        }

        if (summed != new Counters(15, 12, 2, 1))
        {
            failures.Add($"counters across two files should sum to 15/12/2/1 and summed to {summed}");
        }

        var empty = Sum(Path.Combine(root, "absent"), out int none);

        if (none != 0 || empty.Total != 0)
        {
            failures.Add("a root that does not exist should read nothing and total nothing");
        }
    }
    finally
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    Console.WriteLine("Self-test: 2 counted run(s), 1 unfinished file, 1 absent root.");
    Console.WriteLine();

    if (failures.Count > 0)
    {
        Console.WriteLine($"SELF-TEST FAILURES ({failures.Count}):");
        foreach (string failure in failures)
        {
            Console.WriteLine($"  ! {failure}");
        }

        return 1;
    }

    Console.WriteLine("The summary sums what the runs wrote, and a run that wrote nothing totals nothing.");

    return 0;

    static string Trx(int total, int passed, int failed, int notExecuted) =>
        $"""
         <?xml version="1.0" encoding="utf-8"?>
         <TestRun xmlns="{TrxNamespace}">
           <ResultSummary outcome="Completed">
             <Counters total="{total}" executed="{total - notExecuted}" passed="{passed}" failed="{failed}" notExecuted="{notExecuted}" />
           </ResultSummary>
         </TestRun>
         """;
}

internal readonly record struct Counters(int Total, int Passed, int Failed, int Skipped)
{
    public Counters Add(Counters other) =>
        new(Total + other.Total, Passed + other.Passed, Failed + other.Failed, Skipped + other.Skipped);

    public override string ToString() => $"{Total}/{Passed}/{Failed}/{Skipped}";
}

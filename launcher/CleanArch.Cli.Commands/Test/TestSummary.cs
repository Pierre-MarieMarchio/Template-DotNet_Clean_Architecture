using System.Globalization;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using CleanArch.Cli.Commands.Kernel;

namespace CleanArch.Cli.Commands.Test;

internal readonly record struct TestCounters(int Total, int Passed, int Failed, int Skipped)
{
    public TestCounters Add(TestCounters other) =>
        new(Total + other.Total, Passed + other.Passed, Failed + other.Failed, Skipped + other.Skipped);
}

internal static class TestSummary
{
    public const string ResultsFolder = "TestResults";

    private static readonly UTF8Encoding _utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    public static IReadOnlyList<string> ReportOptions => ["--results-directory", ResultsFolder, "--report-xunit-trx"];

    public static void ForgetEarlierReports(string root)
    {
        foreach (string report in Reports(root))
        {
            File.Delete(report);
        }
    }

    public static int Write(Shell shell, string root, IReadOnlyList<string> summaries)
    {
        IReadOnlyList<string> reports = Reports(root);
        TestCounters totals = reports.Aggregate(default(TestCounters), (sum, report) => sum.Add(Read(report)));
        string table = Table(totals);
        shell.Output.WriteLine($"Read {reports.Count} TRX file(s) under {ResultsFolder}.");
        shell.Output.WriteLine();
        shell.Output.Write(table);
        foreach (string summary in summaries)
        {
            File.AppendAllText(summary, table, _utf8NoBom);
        }

        return totals.Total > 0
            ? ExitCode.Succeeded
            : shell.Fail(
                "The run executed no test. dotnet test exits zero when it finds nothing to run, so a test project missing "
                + "from the solution reads exactly like one whose tests all passed: a run with no test fails here.");
    }

    private static string Table(TestCounters totals) =>
        string.Join(
            "\n",
            "### Test results",
            "",
            "| Total | Passed | Failed | Skipped |",
            "|---:|---:|---:|---:|",
            string.Create(CultureInfo.InvariantCulture, $"| {totals.Total} | {totals.Passed} | {totals.Failed} | {totals.Skipped} |"))
        + "\n";

    private static TestCounters Read(string report)
    {
        XElement? counters;
        try
        {
            counters = Child(Child(XDocument.Load(report).Root, "ResultSummary"), "Counters");
        }
        catch (XmlException)
        {
            return default;
        }

        return counters is null
            ? default
            : new TestCounters(Count(counters, "total"), Count(counters, "passed"), Count(counters, "failed"), Count(counters, "notExecuted"));
    }

    private static IReadOnlyList<string> Reports(string root)
    {
        string results = Path.Combine(root, ResultsFolder);
        return Directory.Exists(results)
            ? [.. Directory.EnumerateFiles(results, "*.trx", SearchOption.AllDirectories).Order(StringComparer.Ordinal)]
            : [];
    }

    private static XElement? Child(XElement? parent, string name) =>
        parent?.Elements().FirstOrDefault(element => element.Name.LocalName == name);

    private static int Count(XElement counters, string name) =>
        int.TryParse(counters.Attribute(name)?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) ? value : 0;
}

using System.Text;
using System.Xml;
using CleanArch.Cli.Commands.Kernel;

namespace CleanArch.Cli.Commands.Coverage;

internal sealed record CoverageOutputs(IReadOnlyList<string> Summaries, string? Json)
{
    private static readonly UTF8Encoding _utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    public static CoverageOutputs From(string? summary, string? environmentSummary, string? json) =>
        new([.. new[] { summary, environmentSummary }.OfType<string>().Where(path => path.Length > 0)], string.IsNullOrEmpty(json) ? null : json);

    public void Write(string markdown, string jsonText)
    {
        foreach (string summary in Summaries)
        {
            File.AppendAllText(summary, markdown, _utf8NoBom);
        }

        if (Json is not null)
        {
            File.WriteAllText(Json, jsonText, _utf8NoBom);
        }
    }
}

internal static class CoverageFloor
{
    private const int _badInput = 2;

    public static int Judge(Shell shell, string projectRoot, double minimum, CoverageOutputs outputs)
    {
        string results = Path.Combine(projectRoot, "TestResults");
        string shown = ProjectPath.Relative(projectRoot, results);
        IReadOnlyList<string> reports = CoberturaReports.Find(results);
        if (reports.Count == 0)
        {
            return Reject(shell, "No coverage report", $"No *.cobertura.xml under '{shown}'.");
        }

        IReadOnlyList<AssemblyCoverage> assemblies;
        try
        {
            assemblies = CoberturaReports.Merge(reports);
        }
        catch (XmlException error)
        {
            return Reject(shell, "Unreadable coverage report", error.Message);
        }

        var total = CoverageTotal.Of(assemblies);
        if (total.LinesValid == 0)
        {
            return Reject(
                shell,
                "Coverage report measures nothing",
                $"{reports.Count} report(s) under '{shown}' hold {assemblies.Count} assembly(ies) and not one measurable line. That is a broken report, not full coverage: check that the collector ran and that every product assembly is still in it.");
        }

        string markdown = CoverageSummary.Render(assemblies, total, reports.Count, minimum);
        shell.Output.WriteLine(markdown);
        outputs.Write(markdown, CoverageJson.Render(assemblies, total, [.. reports.Select(report => ProjectPath.Relative(projectRoot, report))], minimum));
        return Compare(shell, total.LineRate, minimum);
    }

    private static int Compare(Shell shell, double lineRate, double minimum)
    {
        string rate = CoverageSummary.Fixed2(lineRate);
        string floor = CoverageSummary.General(minimum);
        if (lineRate + 1e-9 < minimum)
        {
            shell.Error.WriteLine($"::error title=Coverage below floor::{rate}% of lines covered, floor is {floor}%. Cover the new code or argue the floor down in a separate commit.");
            return ExitCode.Failed;
        }

        shell.Output.WriteLine($"Coverage {rate}% >= floor {floor}%.");
        return ExitCode.Succeeded;
    }

    private static int Reject(Shell shell, string title, string message)
    {
        shell.Error.WriteLine($"::error title={title}::{message}");
        return _badInput;
    }
}

using System.Text.RegularExpressions;
using AppTemplate.Architecture.Tests.Fixtures;
using Shouldly;
using Xunit;

namespace AppTemplate.Architecture.Tests.Rules;

public sealed class LogFormatTests
{
    private static readonly Regex _textFormatter = new(
        @"\.(AddConsole|AddSimpleConsole|AddSystemdConsole)\s*\(",
        RegexOptions.None,
        TimeSpan.FromSeconds(5));

    [Fact]
    public void EveryHost_LogsToTheConsoleAsJson_SoAValueWithLineBreaksStaysInsideItsField()
    {
        var hosts = ProjectReferenceGraph.Hosts.ToList();

        hosts.ShouldNotBeEmpty();

        var offenders = new List<string>();

        foreach (var host in hosts)
        {
            string program = Path.Combine(ProjectReferenceGraph.RootOf(host), "Program.cs");
            string text = File.ReadAllText(program);

            if (!text.Contains(".AddJsonConsole(", StringComparison.Ordinal) || _textFormatter.IsMatch(text))
            {
                offenders.Add(host.Name);
            }
        }

        offenders.ShouldBeEmpty(
            "A text console formatter writes a request path or a header value as it came, line breaks " +
            "included, so a caller could forge a log line; the JSON formatter escapes them. These hosts " +
            "do not configure 'AddJsonConsole' alone.");
    }
}

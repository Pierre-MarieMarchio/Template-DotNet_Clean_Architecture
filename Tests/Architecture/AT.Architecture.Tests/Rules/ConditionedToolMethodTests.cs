using System.Text.RegularExpressions;
using AppTemplate.Architecture.Tests.Fixtures;
using Shouldly;
using Xunit;

namespace AppTemplate.Architecture.Tests.Rules;

public sealed class ConditionedToolMethodTests
{
    private const string _tool = "Tools/Tasks.cs";

    private const string _declaredUnderApiCalledOutside =
        """
        internal static class Tasks
        {
            internal static void Verify()
            {
                //#if (api)
                Schema();
                //#endif
                Audit();
            }

            //#if (api)
            private static void Schema()
            {
            }

            private static void Audit()
            {
            }
            //#endif
        }
        """;

    private const string _declaredOutsideEveryBlock =
        """
        internal static class Tasks
        {
            internal static void Verify()
            {
                //#if (api)
                Schema();
                //#endif
                Audit();
            }

            //#if (api)
            private static void Schema()
            {
            }

            //#endif
            private static void Audit()
            {
            }
        }
        """;

    private static readonly TimeSpan _budget = TimeSpan.FromSeconds(5);

    private static readonly Regex _declaration = new(
        @"^\s*(?:(?:private|internal|public|protected|static|async|sealed|partial)\s+)+[\w<>\[\],.?() ]*?\b(?<name>[A-Z]\w*)\s*(?:<[^<>()]*>)?\s*\(",
        RegexOptions.None,
        _budget);

    private static readonly Regex _call = new(@"(?<![\w.])(?<name>[A-Z]\w*)\s*(?:<[^<>()]*>)?\s*\(", RegexOptions.None, _budget);

    private static readonly Lazy<Dictionary<string, string>> _tools = new(() =>
        Directory.EnumerateFiles(Path.Combine(ProjectReferenceGraph.RepositoryRoot, "Tools"), "*.cs")
            .ToDictionary(path => "Tools/" + Path.GetFileName(path), File.ReadAllText, StringComparer.Ordinal));

    private static readonly Lazy<TemplateForms> _forms = new(() => TemplateForms.Load(
        ProjectReferenceGraph.RepositoryRoot,
        [.. _tools.Value.Values
            .SelectMany(text => TemplateMarkers.Conditions(TemplateMarkers.Lines(text)).Values)
            .SelectMany(condition => condition.Names)
            .Distinct(StringComparer.Ordinal)]));

    [Fact]
    public void EveryMethodAToolCalls_IsDeclaredInEachFormThatKeepsTheCall()
    {
        _tools.Value.Keys.ShouldContain(_tool, "Tools holds no Tasks.cs, so this rule reads the wrong tree.");

        _tools.Value.SelectMany(tool => Offenders(tool.Key, tool.Value)).ShouldBeEmpty(
            "A tool calls a method of its own that a marker removes from a form where the call stays, so that form's "
            + "`dotnet run Tools/<tool>.cs` does not compile. Put the method under the condition of its callers, or out of the block.");
    }

    [Fact]
    public void TheAudit_FiresOnAMethodAMarkerRemovesUnderACallItKeeps_AndStaysSilentOnItsFix()
    {
        Offenders(_tool, _declaredUnderApiCalledOutside).ShouldHaveSingleItem().ShouldStartWith(
            $"{_tool}: Audit, declared by no line under ",
            customMessage: "The audit does not fire on a method declared under api and called outside every block.");
        Offenders(_tool, _declaredOutsideEveryBlock).ShouldBeEmpty("The audit fires on a method declared in every form that calls it.");
    }

    private static List<string> Offenders(string path, string text)
    {
        string[] lines = TemplateMarkers.Lines(text);
        var conditions = TemplateMarkers.Conditions(lines);
        var declaredAnywhere = Names(_declaration, lines);
        var offenders = new SortedDictionary<string, string>(StringComparer.Ordinal);

        foreach (var form in _forms.Value.FormsOf(path, conditions.Values.SelectMany(condition => condition.Names)))
        {
            string[] kept = [.. TemplateMarkers.Kept(lines, conditions, form.Value)];
            var declared = Names(_declaration, kept);

            foreach (string missing in Names(_call, kept).Where(name => declaredAnywhere.Contains(name) && !declared.Contains(name)))
            {
                offenders.TryAdd($"{path}: {missing}", form.Options);
            }
        }

        return [.. offenders.Select(offender => $"{offender.Key}, declared by no line under {offender.Value}")];
    }

    private static HashSet<string> Names(Regex pattern, IEnumerable<string> lines) =>
        [.. lines
            .Where(line => !line.TrimStart().StartsWith("//", StringComparison.Ordinal))
            .SelectMany(line => pattern.Matches(line).Select(match => match.Groups["name"].Value))];
}

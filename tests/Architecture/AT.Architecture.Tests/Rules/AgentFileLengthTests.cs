using AppTemplate.Architecture.Tests.Fixtures;
using Shouldly;
using Xunit;

namespace AppTemplate.Architecture.Tests.Rules;

public sealed class AgentFileLengthTests
{
    [Theory]
    [InlineData("AGENTS.md", 125)]
    [InlineData("src/Web/AGENTS.md", 60)]
    public void AnAgentFile_StaysWithinItsCap_InEveryFormItIsGeneratedIn(string path, int cap)
    {
        string root = ProjectReferenceGraph.RepositoryRoot;
        string[] lines = TemplateMarkers.Lines(File.ReadAllText(Path.Combine(root, path)));
        var conditions = TemplateMarkers.Conditions(lines);
        string[] symbols = [.. conditions.Values.SelectMany(condition => condition.Names).Distinct(StringComparer.Ordinal)];
        var forms = TemplateForms.Load(root, symbols).FormsOf(path, symbols).ToList();

        forms.ShouldNotBeEmpty($"No form generates {path}, so this rule reads the wrong tree.");

        var longest = forms
            .Select(form => (Count: Length(TemplateMarkers.Kept(lines, conditions, form.Value).ToList()), form.Options))
            .MaxBy(form => form.Count);

        longest.Count.ShouldBeLessThanOrEqualTo(
            cap,
            $"{path} renders {longest.Count} lines under {longest.Options}: an agent reads it whole before every change, so link to the page that holds a rule "
            + "rather than restate it, as docs/template/maintaining-the-template.md says under 'Writing docs that condition'.");
    }

    private static int Length(List<string> kept) => kept.Count > 0 && kept[^1].Length == 0 ? kept.Count - 1 : kept.Count;
}

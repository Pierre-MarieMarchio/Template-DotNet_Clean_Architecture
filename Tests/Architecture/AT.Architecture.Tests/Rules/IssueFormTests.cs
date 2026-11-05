using System.Text.RegularExpressions;
using AppTemplate.Architecture.Tests.Fixtures;
using Shouldly;
using Xunit;

namespace AppTemplate.Architecture.Tests.Rules;

public sealed class IssueFormTests
{
    private static readonly string[] _inputTypes = ["input", "textarea", "dropdown", "checkboxes"];

    private static readonly Regex _identifier = new(@"^    id: (\S+)$", RegexOptions.Multiline, TimeSpan.FromSeconds(5));

    private static string FormsPath => Path.Combine(ProjectReferenceGraph.RepositoryRoot, ".github", "ISSUE_TEMPLATE");

    [Fact]
    public void TheIssueForms_AskForAReproducibleAndMinimalReport() => AssertForms(FormsPath);

    //#if (templateMaintenance)
    [Fact]
    public void TheRepositoryIssueChooser_SendsQuestionsToDiscussions() =>
        File.ReadAllText(Path.Combine(FormsPath, "config.yml")).ShouldContain("cleanarch-dotnet/discussions", Case.Sensitive, "The repository's issue chooser no longer sends a question to Discussions.");

    //#endif
    internal static void AssertForms(string directory)
    {
        AssertConfig(Path.Combine(directory, "config.yml"));
        AssertForm(Path.Combine(directory, "bug_report.yml"), ["version", "steps", "expected", "actual", "logs"]);
        AssertForm(Path.Combine(directory, "feature_request.yml"), ["need", "proposal"]);
    }

    private static void AssertConfig(string path)
    {
        File.Exists(path).ShouldBeTrue($"{path} is missing.");
        File.ReadAllText(path).ShouldContain("blank_issues_enabled: false", Case.Sensitive, $"{path} no longer closes the empty issue.");
    }

    private static void AssertForm(string path, string[] requiredFields)
    {
        File.Exists(path).ShouldBeTrue($"{path} is missing.");
        string text = File.ReadAllText(path);

        text.ShouldNotContain('\t', $"{path} holds a tab, which YAML refuses as indentation.");
        foreach (string key in new[] { "name", "description", "body" })
        {
            ("\n" + text).ShouldContain($"\n{key}:", Case.Sensitive, $"{path} has no top-level '{key}'.");
        }

        var ids = _identifier.Matches(text).Select(match => match.Groups[1].Value).ToList();
        ids.Distinct().Count().ShouldBe(ids.Count, $"{path} repeats an id, which GitHub refuses.");

        var fields = Regex.Split(text, @"\n  - type: ", RegexOptions.None, TimeSpan.FromSeconds(5)).Skip(1).ToList();
        fields.ShouldNotBeEmpty($"{path} has no field.");
        foreach (string field in fields.Where(field => _inputTypes.Any(type => field.StartsWith(type + "\n", StringComparison.Ordinal))))
        {
            field.ShouldContain("    id: ", Case.Sensitive, $"A field of {path} has no id.");
            field.ShouldContain("      label: ", Case.Sensitive, $"A field of {path} has no label.");
        }

        foreach (string id in requiredFields)
        {
            var block = Regex.Match(text, $@"    id: {id}\n(?:(?!\n  - type: )[\s\S])*", RegexOptions.None, TimeSpan.FromSeconds(5));
            block.Success.ShouldBeTrue($"{path} no longer has the '{id}' field.");
            block.Value.ShouldContain("required: true", Case.Sensitive, $"The '{id}' field of {path} is no longer required.");
        }
    }
}

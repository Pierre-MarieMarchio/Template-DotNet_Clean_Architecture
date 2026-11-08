using AppTemplate.Architecture.Tests.Fixtures;
using Shouldly;
using Xunit;

namespace AppTemplate.Architecture.Tests.Rules;

public sealed class PullRequestTemplateTests
{
    private const string _rule = "CONTRIBUTING.md";

    private static string TemplatePath => Path.Combine(
        ProjectReferenceGraph.RepositoryRoot, ".github", "pull_request_template.md");

    [Fact]
    public void ThePullRequestTemplate_CarriesTheAuthorshipRuleAndWhereItIsWritten() => AssertCarriesTheAuthorshipRule(TemplatePath);

    internal static void AssertCarriesTheAuthorshipRule(string path)
    {
        File.Exists(path).ShouldBeTrue($"{path} is missing: every pull request opens from it.");

        string text = File.ReadAllText(path);

        text.ShouldContain("## The problem, and why this change solves it", Case.Sensitive, "The template no longer asks for the problem before the change.");
        text.ShouldContain("## The one concern", Case.Sensitive, "The template no longer asks for one concern per pull request.");
        text.ShouldContain("<type>(<scope>): <summary>", Case.Sensitive, "The template no longer states the commit subject form.");
        text.ShouldContain("No commit or description names an AI tool", Case.Sensitive, "The template's checklist no longer says that a pull request names no AI tool.");
        text.ShouldContain("## Assistance", Case.Sensitive, "The template no longer has the Assistance section.");
        text.ShouldContain("Assisted-by:", Case.Sensitive, "The template no longer names the Assisted-by trailer.");
        text.ShouldContain("I answer for every line", Case.Sensitive, "The template's checklist no longer makes the author answer for every line.");
        text.ShouldContain($"`{_rule}`", Case.Sensitive, "The template no longer points to the page that holds the rule.");
        File.Exists(Path.Combine(ProjectReferenceGraph.RepositoryRoot, _rule)).ShouldBeTrue($"{_rule} does not exist.");
    }
}

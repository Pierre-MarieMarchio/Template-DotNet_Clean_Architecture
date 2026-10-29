using AppTemplate.Architecture.Tests.Fixtures;
using Shouldly;
using Xunit;

namespace AppTemplate.Architecture.Tests.Rules;

public sealed class PullRequestTemplateTests
{
    private const string _decision = "docs/decisions/0028-the-author-answers-for-every-line.md";

    private static string TemplatePath => Path.Combine(
        ProjectReferenceGraph.RepositoryRoot, ".github", "pull_request_template.md");

    [Fact]
    public void ThePullRequestTemplate_CarriesTheAuthorshipRuleAndItsDecision()
    {
        File.Exists(TemplatePath).ShouldBeTrue(".github/pull_request_template.md is missing: every pull request opens from it.");

        string text = File.ReadAllText(TemplatePath);

        text.ShouldContain("No commit or description names an AI tool", Case.Sensitive, "The template's checklist no longer says that a pull request names no AI tool.");
        text.ShouldContain("## Assistance", Case.Sensitive, "The template no longer has the Assistance section.");
        text.ShouldContain("Assisted-by:", Case.Sensitive, "The template no longer names the Assisted-by trailer.");
        text.ShouldContain("I answer for every line", Case.Sensitive, "The template's checklist no longer makes the author answer for every line.");
        text.ShouldContain(_decision, Case.Sensitive, "The template no longer points to the decision that holds the rule.");
        File.Exists(Path.Combine(ProjectReferenceGraph.RepositoryRoot, _decision)).ShouldBeTrue($"{_decision} does not exist.");
    }
}

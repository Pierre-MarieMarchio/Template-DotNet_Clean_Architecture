using CleanArch.Create.Manifests;
using CleanArch.Create.Prompts;
using Shouldly;
using Spectre.Console.Testing;
using Xunit;

namespace CleanArch.Create.Tests.Prompts;

public sealed class QuestionnaireTests
{
    private static readonly Dictionary<string, string> _nothing = [];

    private readonly TemplateManifest _project = Repository.Source.Manifest(TemplateKind.Project);

    [Fact]
    public void WithoutATerminal_EveryAnswerNotGiven_IsNamed()
    {
        var questionnaire = new Questionnaire(new TestConsole(), interactive: false, yes: false);

        LauncherException error = Should.Throw<LauncherException>(() =>
            questionnaire.Answer(_project, new Dictionary<string, string> { ["auth"] = "local" }, _nothing, Launcher.ProjectAskedWhen));

        error.Message.ShouldContain("--examples");
        error.Message.ShouldContain("--web");
        error.Message.ShouldNotContain("--auth");
        error.Message.ShouldNotContain("--web-name");
    }

    [Fact]
    public void Yes_TakesEachDefault_AndSkipsWhatOnlyAWebAppAsks()
    {
        var questionnaire = new Questionnaire(new TestConsole(), interactive: true, yes: true);

        Dictionary<string, string> answers = questionnaire.Answer(_project, _nothing, _nothing, Launcher.ProjectAskedWhen);

        answers["auth"].ShouldBe("local");
        answers["web"].ShouldBe("none");
        answers.ShouldNotContainKey("webName");
        answers.ShouldNotContainKey("webAudience");
        _project.CommandArguments(answers).ShouldBeEmpty();
    }

    [Fact]
    public void Yes_AsksForTheWebAppsName_OnceThereIsOne()
    {
        var questionnaire = new Questionnaire(new TestConsole(), interactive: false, yes: true);

        Dictionary<string, string> answers = questionnaire.Answer(
            _project, new Dictionary<string, string> { ["web"] = "react", ["web-name"] = "Shop" }, _nothing, Launcher.ProjectAskedWhen);

        _project.CommandArguments(answers).ShouldBe(["--web", "react", "--web-name", "Shop"]);
    }

    [Fact]
    public void WithoutATerminal_ARefusedCombination_IsExplainedAndNotGenerated()
    {
        var questionnaire = new Questionnaire(new TestConsole(), interactive: false, yes: true);

        LauncherException error = Should.Throw<LauncherException>(() =>
            questionnaire.Answer(_project, new Dictionary<string, string> { ["auth"] = "none", ["files"] = "false" }, _nothing, Launcher.ProjectAskedWhen));

        error.Message.ShouldContain(_project.Refusals.Single(refusal => refusal.Symbol == "authNoneNeedsExamplesNone").Explanation);
        error.Message.ShouldNotContain("--files false.");
    }

    [Theory]
    [InlineData("--auth", "oidc")]
    [InlineData("--files", "maybe")]
    [InlineData("--colour", "blue")]
    public void AGivenAnswer_TheTemplateCannotTake_IsRefused(string flag, string value)
    {
        var questionnaire = new Questionnaire(new TestConsole(), interactive: false, yes: true);

        Should.Throw<LauncherException>(() =>
            questionnaire.Answer(_project, new Dictionary<string, string> { [flag[2..]] = value }, _nothing, Launcher.ProjectAskedWhen));
    }

    [Fact]
    public void InATerminal_ARefusedCombination_IsExplained_ThenItsOptionsAreAskedAgain()
    {
        var console = new TestConsole().Interactive();
        console.Profile.Width = 200;
        console.Input.PushKey(ConsoleKey.Enter);
        console.Input.PushKey(ConsoleKey.Enter);
        var questionnaire = new Questionnaire(console, interactive: true, yes: false);

        Dictionary<string, string> answers = questionnaire.Answer(
            _project,
            new Dictionary<string, string> { ["auth"] = "none", ["database"] = "postgresql", ["examples"] = "all", ["files"] = "false", ["ci"] = "true", ["sonar"] = "true", ["agent-files"] = "true", ["web"] = "none" },
            _nothing,
            Launcher.ProjectAskedWhen);

        console.Output.ShouldContain("--auth none refuses the example features");
        answers["auth"].ShouldBe("none");
        answers["examples"].ShouldBe("none");
        _project.RefusalsFor(answers).ShouldBeEmpty();
    }

    [Fact]
    public void InATerminal_EachOptionNotGiven_IsAsked_WithTheTemplatesDefault()
    {
        var console = new TestConsole().Interactive();
        console.Profile.Width = 200;
        console.Input.PushTextWithEnter("n");
        console.Input.PushKey(ConsoleKey.Enter);
        console.Input.PushKey(ConsoleKey.Enter);
        console.Input.PushKey(ConsoleKey.Enter);
        var questionnaire = new Questionnaire(console, interactive: true, yes: false);

        Dictionary<string, string> answers = questionnaire.Answer(
            _project,
            new Dictionary<string, string> { ["auth"] = "local", ["database"] = "postgresql", ["examples"] = "all", ["files"] = "true", ["ci"] = "true" },
            _nothing,
            Launcher.ProjectAskedWhen);

        console.Output.ShouldContain("Sonar analysis");
        console.Output.ShouldContain("Agent instructions");
        console.Output.ShouldContain("First web app");
        answers["sonar"].ShouldBe("false");
        answers["agentFiles"].ShouldBe("true");
        answers["web"].ShouldBe("none");
        answers["presentation"].ShouldBe("none");
        answers.ShouldNotContainKey("presentationName");
    }

    [Fact]
    public void InATerminal_APresentationNameTheTemplateUses_IsExplained_ThenAskedAgain()
    {
        var console = new TestConsole().Interactive();
        console.Profile.Width = 200;
        console.Input.PushKey(ConsoleKey.DownArrow);
        console.Input.PushKey(ConsoleKey.Enter);
        console.Input.PushTextWithEnter("Api");
        console.Input.PushKey(ConsoleKey.Enter);
        console.Input.PushTextWithEnter("Portal");
        var questionnaire = new Questionnaire(console, interactive: true, yes: false);

        Dictionary<string, string> answers = questionnaire.Answer(
            _project,
            new Dictionary<string, string> { ["auth"] = "local", ["database"] = "postgresql", ["examples"] = "all", ["files"] = "true", ["ci"] = "true", ["sonar"] = "true", ["agent-files"] = "true", ["web"] = "none" },
            _nothing,
            Launcher.ProjectAskedWhen);

        console.Output.ShouldContain(_project.Refusals.Single(refusal => refusal.Symbol == "presentationNameIsTaken").Explanation);
        answers["presentation"].ShouldBe("blazor-server");
        answers["presentationName"].ShouldBe("Portal");
        _project.RefusalsFor(answers).ShouldBeEmpty();
    }

    [Fact]
    public void InATerminal_MySql_IsNeverOffered()
    {
        var console = new TestConsole().Interactive();
        console.Profile.Width = 200;
        console.Input.PushKey(ConsoleKey.Enter);
        console.Input.PushKey(ConsoleKey.Enter);
        var questionnaire = new Questionnaire(console, interactive: true, yes: false);

        Dictionary<string, string> answers = questionnaire.Answer(
            _project,
            new Dictionary<string, string> { ["auth"] = "local", ["examples"] = "all", ["files"] = "true", ["ci"] = "true", ["sonar"] = "true", ["agent-files"] = "true", ["web"] = "none" },
            _nothing,
            Launcher.ProjectAskedWhen);

        console.Output.ShouldContain("postgresql");
        console.Output.ShouldNotContain("mysql", Case.Sensitive);
        answers["database"].ShouldBe("postgresql");
    }

    [Fact]
    public void InATerminal_AuthNone_AnswersWhatItRefuses_AndOffersNoAdministrators()
    {
        var console = new TestConsole().Interactive();
        console.Profile.Width = 200;
        console.Input.PushKey(ConsoleKey.DownArrow);
        console.Input.PushKey(ConsoleKey.DownArrow);
        console.Input.PushKey(ConsoleKey.Enter);
        console.Input.PushKey(ConsoleKey.Enter);
        console.Input.PushKey(ConsoleKey.Enter);
        console.Input.PushKey(ConsoleKey.Enter);
        console.Input.PushKey(ConsoleKey.Enter);
        console.Input.PushKey(ConsoleKey.DownArrow);
        console.Input.PushKey(ConsoleKey.Enter);
        console.Input.PushKey(ConsoleKey.Enter);
        console.Input.PushKey(ConsoleKey.Enter);
        var questionnaire = new Questionnaire(console, interactive: true, yes: false);

        Dictionary<string, string> answers = questionnaire.Answer(_project, _nothing, _nothing, Launcher.ProjectAskedWhen);

        answers["auth"].ShouldBe("none");
        answers["examples"].ShouldBe("none");
        answers["files"].ShouldBe("false");
        answers["web"].ShouldBe("placeholder");
        answers["webAudience"].ShouldBe("public");
        _project.RefusalsFor(answers).ShouldBeEmpty();
        console.Output.ShouldContain(_project.Refusals.Single(refusal => refusal.Symbol == "authNoneNeedsExamplesNone").Explanation);
        console.Output.ShouldContain(_project.Refusals.Single(refusal => refusal.Symbol == "adminWebNeedsAccounts").Explanation);
        _project.CommandArguments(answers).ShouldBe(["--auth", "none", "--files", "false", "--examples", "none", "--web", "placeholder"], ignoreOrder: true);
    }

    [Fact]
    public void InATerminal_AWebAppOfAPublicApi_IsForItsUsers()
    {
        TemplateManifest webApp = Repository.Source.Manifest(TemplateKind.WebApp);
        var console = new TestConsole().Interactive();
        console.Profile.Width = 200;
        console.Input.PushKey(ConsoleKey.Enter);
        console.Input.PushKey(ConsoleKey.Enter);
        console.Input.PushKey(ConsoleKey.Enter);
        var questionnaire = new Questionnaire(console, interactive: true, yes: false);

        Dictionary<string, string> answers = questionnaire.Answer(
            webApp,
            _nothing,
            new Dictionary<string, string> { ["auth"] = "none", ["ci"] = "true", ["agentFiles"] = "true", ["scope"] = "acme-shop" },
            new Dictionary<string, Condition>());

        answers["audience"].ShouldBe("public");
        console.Output.ShouldNotContain("A back office");
        console.Output.ShouldContain(webApp.Refusals.Single().Explanation);
    }
}

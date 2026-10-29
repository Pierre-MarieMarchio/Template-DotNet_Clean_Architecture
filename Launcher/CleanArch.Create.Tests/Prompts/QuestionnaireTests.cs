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
        answers.ShouldNotContainKey("webRendering");
        _project.CommandArguments(answers).ShouldBeEmpty();
    }

    [Fact]
    public void Yes_TakesTheOnlyValueTheTemplateLeaves_AsTheQuestionsDo()
    {
        var console = new TestConsole();
        console.Profile.Width = Terminal.UnwrappedWidth;
        var questionnaire = new Questionnaire(console, interactive: false, yes: true);

        Dictionary<string, string> answers = questionnaire.Answer(
            _project, new Dictionary<string, string> { ["auth"] = "none" }, _nothing, Launcher.ProjectAskedWhen);

        answers["examples"].ShouldBe("none");
        answers["files"].ShouldBe("false");
        answers["database"].ShouldBe("postgresql");
        _project.RefusalsFor(answers).ShouldBeEmpty();
        console.Output.ShouldContain("Example features: none (the only option when Authentication is none)");
    }

    [Fact]
    public void Yes_AsksForTheWebAppsName_OnceThereIsOne()
    {
        var questionnaire = new Questionnaire(new TestConsole(), interactive: false, yes: true);

        Dictionary<string, string> answers = questionnaire.Answer(
            _project, new Dictionary<string, string> { ["web"] = "vite-react", ["web-name"] = "Shop" }, _nothing, Launcher.ProjectAskedWhen);

        _project.CommandArguments(answers).ShouldBe(["--web", "vite-react", "--web-name", "Shop"]);
    }

    [Fact]
    public void Yes_AnOptionWithoutADefault_IsNamed_AsOneYesCannotChoose()
    {
        TemplateManifest presentation = Repository.Source.Manifest(TemplateKind.Presentation);
        var questionnaire = new Questionnaire(new TestConsole(), interactive: false, yes: true);
        var given = new Dictionary<string, string> { ["project-name"] = "Acme.Shop", ["auth"] = "local", ["examples"] = "all", ["files"] = "true", ["database"] = "postgresql", ["agent-files"] = "true", ["has-client"] = "false" };

        LauncherException error = Should.Throw<LauncherException>(() => questionnaire.Answer(presentation, given, _nothing, new Dictionary<string, Condition>()));

        error.Message.ShouldBe("--yes can't choose these, which have no default: --framework. Pass them as options.");
    }

    [Fact]
    public void WithoutATerminal_ARefusedCombination_IsExplainedAndNotGenerated()
    {
        var questionnaire = new Questionnaire(new TestConsole(), interactive: false, yes: true);

        LauncherException error = Should.Throw<LauncherException>(() =>
            questionnaire.Answer(
                _project, new Dictionary<string, string> { ["auth"] = "none", ["examples"] = "all", ["files"] = "false" }, _nothing, Launcher.ProjectAskedWhen));

        error.Message.ShouldContain(_project.Refusals.Single(refusal => refusal.Symbol == "authNoneNeedsExamplesNone").Explanation);
        error.Message.ShouldNotContain("--files false.");
    }

    [Fact]
    public void WithoutATerminal_AChoiceOfOneValue_IsTakenWithoutAFlag_AndSaysWhy()
    {
        TemplateManifest presentation = Repository.Source.Manifest(TemplateKind.Presentation);
        var console = new TestConsole();
        console.Profile.Width = Terminal.UnwrappedWidth;
        var questionnaire = new Questionnaire(console, interactive: false, yes: false);
        var given = new Dictionary<string, string> { ["project-name"] = "Acme.Shop", ["auth"] = "none", ["examples"] = "none", ["files"] = "false", ["database"] = "postgresql", ["agent-files"] = "true", ["has-client"] = "false", ["has-api"] = "true" };

        Dictionary<string, string> answers = questionnaire.Answer(presentation, given, _nothing, new Dictionary<string, Condition>());

        presentation.CommandArguments(answers).ShouldBe(["--framework", "blazor-server", "--project-name", "Acme.Shop", "--auth", "none", "--examples", "none", "--files", "false"]);
        console.Output.ShouldContain("Framework: blazor-server (the only option when Project authentication is none)");
    }

    [Theory]
    [InlineData("--auth", "oidc")]
    [InlineData("--files", "maybe")]
    [InlineData("--colour", "blue")]
    [InlineData("--presentation", "api|desktop")]
    public void AGivenAnswer_TheTemplateCannotTake_IsRefused(string flag, string value)
    {
        var questionnaire = new Questionnaire(new TestConsole(), interactive: false, yes: true);

        Should.Throw<LauncherException>(() =>
            questionnaire.Answer(_project, new Dictionary<string, string> { [flag[2..]] = value }, _nothing, Launcher.ProjectAskedWhen));
    }

    [Fact]
    public void AnOptionGivenTwice_IsRefused_UnlessItTakesSeveralValues()
    {
        var questionnaire = new Questionnaire(new TestConsole(), interactive: false, yes: true);

        LauncherException error = Should.Throw<LauncherException>(() =>
            questionnaire.Answer(_project, new Dictionary<string, string> { ["web"] = "vite-react", ["web-name"] = "Shop|Admin" }, _nothing, Launcher.ProjectAskedWhen));

        error.Message.ShouldBe("--web-name takes one value, not Shop and Admin.");
    }

    [Fact]
    public void InATerminal_ARefusedCombination_IsExplained_ThenItsOptionsAreAskedAgain()
    {
        var console = new TestConsole().Interactive();
        console.Profile.Width = 200;
        console.Input.PushKey(ConsoleKey.Enter);
        console.Input.PushKey(ConsoleKey.Enter);
        console.Input.PushKey(ConsoleKey.Enter);
        var questionnaire = new Questionnaire(console, interactive: true, yes: false);

        Dictionary<string, string> answers = questionnaire.Answer(
            _project,
            new Dictionary<string, string> { ["auth"] = "none", ["database"] = "postgresql", ["examples"] = "all", ["files"] = "false", ["ci"] = "true", ["sonar"] = "true", ["agent-files"] = "true", ["web"] = "none" },
            _nothing,
            Launcher.ProjectAskedWhen);

        console.Output.ShouldContain("--auth none refuses the example features");
        console.Output.ShouldContain("Asking again: Authentication, Example features.");
        answers["auth"].ShouldBe("none");
        answers["examples"].ShouldBe("none");
        _project.RefusalsFor(answers).ShouldBeEmpty();
    }

    [Fact]
    public void InATerminal_EachOptionNotGiven_IsAsked_WithTheTemplatesDefault()
    {
        var console = new TestConsole().Interactive();
        console.Profile.Width = 200;
        console.Input.PushKey(ConsoleKey.Enter);
        console.Input.PushKey(ConsoleKey.Enter);
        console.Input.PushTextWithEnter("n");
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
        answers["presentation"].ShouldBe("api");
    }

    [Fact]
    public void InATerminal_TwoDesktopFrameworks_AreExplained_ThenTheSelectionIsAskedAgain()
    {
        var console = new TestConsole().Interactive();
        console.Profile.Width = 200;
        PushKeys(console, ConsoleKey.DownArrow, ConsoleKey.DownArrow, ConsoleKey.DownArrow, ConsoleKey.Spacebar, ConsoleKey.DownArrow, ConsoleKey.Spacebar, ConsoleKey.Enter);
        PushKeys(console, ConsoleKey.DownArrow, ConsoleKey.DownArrow, ConsoleKey.DownArrow, ConsoleKey.DownArrow, ConsoleKey.Spacebar, ConsoleKey.Enter);
        PushKeys(console, ConsoleKey.Enter);
        var questionnaire = new Questionnaire(console, interactive: true, yes: false);

        Dictionary<string, string> answers = questionnaire.Answer(
            _project,
            new Dictionary<string, string> { ["auth"] = "local", ["database"] = "postgresql", ["examples"] = "all", ["files"] = "true", ["ci"] = "true", ["agent-files"] = "true", ["web"] = "none" },
            _nothing,
            Launcher.ProjectAskedWhen);

        string refused = _project.Refusals.Single(refusal => refusal.Symbol == "desktopIsOneFramework").Explanation + Environment.NewLine + "Asking again: Presentation projects.";
        console.Output.IndexOf(refused, StringComparison.Ordinal).ShouldBeInRange(0, console.Output.IndexOf("Sonar analysis", StringComparison.Ordinal), "The selection is explained and asked again before the next question.");
        answers["presentation"].ShouldBe("api|avalonia");
        _project.RefusalsFor(answers).ShouldBeEmpty();
    }

    [Fact]
    public void Yes_SeveralPresentationProjects_ArePassedAsOneOptionPerValue_InTheTemplatesOrder()
    {
        var questionnaire = new Questionnaire(new TestConsole(), interactive: false, yes: true);

        Dictionary<string, string> answers = questionnaire.Answer(_project, new Dictionary<string, string> { ["presentation"] = "mcp|api" }, _nothing, Launcher.ProjectAskedWhen);

        answers["presentation"].ShouldBe("api|mcp");
        _project.CommandArguments(answers).ShouldBe(["--presentation", "api", "--presentation", "mcp"]);
    }

    [Theory]
    [InlineData("avalonia|wpf", "desktopIsOneFramework")]
    [InlineData("blazor-server", null)]
    [InlineData("api|worker", null)]
    public void WithoutATerminal_PresentationProjectsTheTemplateRefusesTogether_AreExplainedAndNotGenerated(string presentation, string? refusal)
    {
        var questionnaire = new Questionnaire(new TestConsole(), interactive: false, yes: true);
        var given = new Dictionary<string, string> { ["presentation"] = presentation };

        if (refusal is null)
        {
            _project.RefusalsFor(questionnaire.Answer(_project, given, _nothing, Launcher.ProjectAskedWhen)).ShouldBeEmpty();
            return;
        }

        LauncherException error = Should.Throw<LauncherException>(() => questionnaire.Answer(_project, given, _nothing, Launcher.ProjectAskedWhen));

        error.Message.ShouldContain(_project.Refusals.Single(candidate => candidate.Symbol == refusal).Explanation);
    }

    [Fact]
    public void InATerminal_MySql_IsNeverOffered()
    {
        var console = new TestConsole().Interactive();
        console.Profile.Width = 200;
        console.Input.PushKey(ConsoleKey.Enter);
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
    public void InATerminal_AuthNone_AnswersWhatItRefuses()
    {
        var console = new TestConsole().Interactive();
        console.Profile.Width = 200;
        console.Input.PushKey(ConsoleKey.Enter);
        console.Input.PushKey(ConsoleKey.DownArrow);
        console.Input.PushKey(ConsoleKey.Enter);
        console.Input.PushKey(ConsoleKey.Enter);
        console.Input.PushKey(ConsoleKey.Enter);
        console.Input.PushKey(ConsoleKey.DownArrow);
        console.Input.PushKey(ConsoleKey.DownArrow);
        console.Input.PushKey(ConsoleKey.Enter);
        console.Input.PushKey(ConsoleKey.Enter);
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
        console.Output.ShouldContain("none · the only option when Authentication is none");
        console.Output.ShouldNotContain("add --examples none");
        _project.CommandArguments(answers).ShouldBe(["--auth", "none", "--files", "false", "--examples", "none", "--web", "placeholder"], ignoreOrder: true);
    }

    [Fact]
    public void InATerminal_ABackOfficeChosenFirst_LeavesNoPublicApiToChoose()
    {
        var console = new TestConsole().Interactive();
        console.Profile.Width = 200;
        console.Input.PushKey(ConsoleKey.Enter);
        console.Input.PushKey(ConsoleKey.DownArrow);
        console.Input.PushKey(ConsoleKey.Enter);
        console.Input.PushKey(ConsoleKey.Enter);
        console.Input.PushKey(ConsoleKey.DownArrow);
        console.Input.PushKey(ConsoleKey.Enter);
        for (int index = 0; index < 8; index++)
        {
            console.Input.PushKey(ConsoleKey.Enter);
        }

        var questionnaire = new Questionnaire(console, interactive: true, yes: false);

        Dictionary<string, string> answers = questionnaire.Answer(_project, _nothing, _nothing, Launcher.ProjectAskedWhen);

        answers["webAudience"].ShouldBe("admin");
        answers["auth"].ShouldBe("local");
        console.Output.ShouldNotContain(_project.Options.Single(option => option.Symbol == "auth").Choices.Single(choice => choice.Value == "none").Description);
        _project.RefusalsFor(answers).ShouldBeEmpty();
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
            new Dictionary<string, string>
            {
                ["auth"] = "none",
                ["examples"] = "none",
                ["files"] = "false",
                ["hasClient"] = "false",
                ["ci"] = "true",
                ["agentFiles"] = "true",
                ["scope"] = "acme-shop",
                ["projectName"] = "Acme.Shop",
            },
            new Dictionary<string, Condition>());

        answers["audience"].ShouldBe("public");
        console.Output.ShouldNotContain("A back office");
        console.Output.ShouldContain("public · the only option when Project authentication is none");
    }

    private static void PushKeys(TestConsole console, params ConsoleKey[] keys)
    {
        foreach (ConsoleKey key in keys)
        {
            console.Input.PushKey(key);
        }
    }
}

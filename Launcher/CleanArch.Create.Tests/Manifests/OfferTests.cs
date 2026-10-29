using CleanArch.Create.Manifests;
using Shouldly;
using Xunit;

namespace CleanArch.Create.Tests.Manifests;

public sealed class OfferTests
{
    private static readonly Dictionary<string, Condition> _alwaysAsked = [];

    private readonly TemplateManifest _project = Repository.Source.Manifest(TemplateKind.Project);
    private readonly TemplateManifest _webApp = Repository.Source.Manifest(TemplateKind.WebApp);

    [Fact]
    public void MySql_IsNeverOffered_ThoughTheTemplateKeepsItAsAChoice()
    {
        _project.Options.Single(option => option.Symbol == "database").Choices.ShouldContain(choice => choice.Value == "mysql");

        Offer offer = Offer.For(_project, Option(_project, "database"), new Dictionary<string, string>(), Launcher.ProjectAskedWhen);

        offer.Values.ShouldBe(["postgresql", "sqlserver", "sqlite"]);
        offer.Reasons.Select(refusal => refusal.Symbol).ShouldBe(["mySqlNotYetGenerated"]);
    }

    [Theory]
    [InlineData("examples", "none", "authNoneNeedsExamplesNone")]
    [InlineData("files", Condition.False, "authNoneNeedsFilesFalse")]
    [InlineData("webAudience", "public", "adminWebNeedsAccounts")]
    public void AuthNone_LeavesOneValue_AndTheRefusalThatSaysWhy(string symbol, string value, string refusal)
    {
        var known = new Dictionary<string, string> { ["auth"] = "none", ["web"] = "vite-react" };

        Offer offer = Offer.For(_project, Option(_project, symbol), known, Launcher.ProjectAskedWhen);

        offer.Values.ShouldBe([value]);
        offer.Reasons.Select(reason => reason.Symbol).ShouldBe([refusal]);
    }

    [Fact]
    public void AnAnswerGivenFirst_ConstrainsTheQuestionsAskedBeforeIt()
    {
        var known = new Dictionary<string, string> { ["examples"] = "notes" };

        Offer.For(_project, Option(_project, "auth"), known, Launcher.ProjectAskedWhen).Values.ShouldBe(["local", "external"]);
    }

    [Fact]
    public void AnAdminWebApp_LeavesNoneToAnAccountlessApi_OnlyOnceThereIsAWebApp()
    {
        var known = new Dictionary<string, string> { ["examples"] = "none", ["files"] = Condition.False, ["webAudience"] = "admin" };

        Offer.For(_project, Option(_project, "auth"), known, Launcher.ProjectAskedWhen).Values.ShouldBe(["local", "external", "none"]);
        Offer.For(_project, Option(_project, "auth"), new Dictionary<string, string>(known) { ["web"] = "vite-react" }, Launcher.ProjectAskedWhen)
            .Values.ShouldBe(["local", "external"]);
    }

    [Fact]
    public void AWebAppOfAnAccountlessApi_IsOfferedThePublicAudienceOnly()
    {
        var known = new Dictionary<string, string> { ["auth"] = "none" };

        Offer offer = Offer.For(_webApp, Option(_webApp, "audience"), known, _alwaysAsked);

        offer.Values.ShouldBe(["public"]);
        offer.Reasons.ShouldBe([_webApp.Refusals.Single(refusal => refusal.Symbol == "adminNeedsAccounts")]);
        Offer.For(_webApp, Option(_webApp, "audience"), new Dictionary<string, string> { ["auth"] = "local" }, _alwaysAsked)
            .Values.ShouldBe(["public", "admin"]);
    }

    [Fact]
    public void ServerRendering_IsLeftOut_WhereNoAnswerGeneratesIt()
    {
        var known = new Dictionary<string, string> { ["web"] = "vite-react", ["auth"] = "none" };

        Offer offer = Offer.For(_project, Option(_project, "webRendering"), known, Launcher.ProjectAskedWhen);

        offer.Values.ShouldBe(["spa"]);
        offer.Reasons.Select(reason => reason.Symbol).ShouldBe(["serverRenderingNeedsAServerStarter"]);
    }

    [Theory]
    [InlineData("angular", "local")]
    [InlineData("angular", "external")]
    [InlineData("angular", "none")]
    [InlineData("react-router", "local")]
    [InlineData("react-router", "external")]
    [InlineData("react-router", "none")]
    public void ServerRendering_IsOffered_ForAStarterThatRendersOnTheServer_WhateverTheProjectAuthenticates(string framework, string auth)
    {
        var known = new Dictionary<string, string> { ["auth"] = auth };

        Offer.For(_project, Option(_project, "webRendering"), new Dictionary<string, string>(known) { ["web"] = framework }, Launcher.ProjectAskedWhen)
            .Values.ShouldBe(["spa", "ssr"]);
        Offer.For(_webApp, Option(_webApp, "rendering"), new Dictionary<string, string>(known) { ["framework"] = framework }, _alwaysAsked)
            .Values.ShouldBe(["spa", "ssr"]);
        Offer.For(_webApp, Option(_webApp, "rendering"), new Dictionary<string, string>(known) { ["framework"] = "vite-vue" }, _alwaysAsked)
            .Values.ShouldBe(["spa"]);
    }

    [Fact]
    public void ATemplateWithoutRefusals_OffersEveryChoice()
    {
        TemplateManifest webPackage = Repository.Source.Manifest(TemplateKind.WebPackage);

        webPackage.Refusals.ShouldBeEmpty();
        webPackage.Options.ShouldAllBe(option => Offer.For(webPackage, option, new Dictionary<string, string>(), _alwaysAsked).Reasons.Count == 0);
    }

    [Fact]
    public void AnsweringOnlyWhatIsOffered_InTheQuestionsOrder_NeverEndsInARefusal()
    {
        int ends = 0;
        var pending = new Stack<(int Next, Dictionary<string, string> Answers)>([(0, new Dictionary<string, string>(StringComparer.Ordinal))]);

        while (pending.TryPop(out (int Next, Dictionary<string, string> Answers) step))
        {
            if (step.Next == _project.Options.Count)
            {
                _project.RefusalsFor(step.Answers).ShouldBeEmpty(string.Join(", ", step.Answers.Select(answer => $"{answer.Key}={answer.Value}")));
                ends++;
                continue;
            }

            TemplateOption option = _project.Options[step.Next];
            bool asked = !Launcher.ProjectAskedWhen.TryGetValue(option.Symbol, out Condition? when) || _project.Holds(when, step.Answers);
            IReadOnlyList<string> values = asked ? Offer.For(_project, option, step.Answers, Launcher.ProjectAskedWhen).Values : [];

            if (values.Count == 0)
            {
                pending.Push((step.Next + 1, step.Answers));
            }

            foreach (string value in values)
            {
                pending.Push((step.Next + 1, new Dictionary<string, string>(step.Answers) { [option.Symbol] = value }));
            }
        }

        ends.ShouldBeGreaterThan(100);
    }

    private static TemplateOption Option(TemplateManifest manifest, string symbol) =>
        manifest.Options.Single(option => option.Symbol == symbol);
}

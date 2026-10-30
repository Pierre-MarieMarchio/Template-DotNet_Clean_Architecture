using CleanArch.Tooling.Manifests;
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
            IReadOnlyList<string> offered = asked ? Offer.For(_project, option, step.Answers, Launcher.ProjectAskedWhen).Values : [];
            IReadOnlyList<string> values = option.AllowsMany ? Accepted(option, offered, step.Answers) : offered;

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

    [Fact]
    public void EachDesktopFramework_IsOffered_ThoughBothTogetherAreRefusedOnceChosen()
    {
        var known = new Dictionary<string, string> { ["auth"] = "local" };

        Offer.For(_project, Option(_project, "presentation"), known, Launcher.ProjectAskedWhen).Values
            .ShouldBe(["api", "blazor-server", "mcp", "avalonia", "wpf", "worker"]);
        Offer.Unavoidable(_project, new Dictionary<string, string>(known) { ["presentation"] = "avalonia|wpf" }, Launcher.ProjectAskedWhen)
            .Select(refusal => refusal.Symbol).ShouldBe(["desktopIsOneFramework"]);
        Offer.Unavoidable(_project, new Dictionary<string, string>(known) { ["presentation"] = "api|wpf" }, Launcher.ProjectAskedWhen).ShouldBeEmpty();
    }

    [Fact]
    public void APortalOrAnMcpServer_IsOffered_WithoutAWebApp_ForTheApiBesideIt()
    {
        var known = new Dictionary<string, string> { ["web"] = "none", ["auth"] = "external" };

        Offer.For(_project, Option(_project, "presentation"), known, Launcher.ProjectAskedWhen).Values
            .ShouldBe(["api", "blazor-server", "mcp", "avalonia", "wpf", "worker"]);
        Offer.Unavoidable(_project, new Dictionary<string, string>(known) { ["presentation"] = "blazor-server|mcp" }, Launcher.ProjectAskedWhen)
            .ShouldBeEmpty();
    }

    [Fact]
    public void AnAccountlessProject_IsOfferedNoPresentationThatNeedsAccounts()
    {
        Offer offer = Offer.For(_project, Option(_project, "presentation"), new Dictionary<string, string> { ["auth"] = "none" }, Launcher.ProjectAskedWhen);

        offer.Values.ShouldBe(["api", "blazor-server", "worker"]);
        offer.Reasons.Select(refusal => refusal.Symbol).ShouldBe(["clientPresentationNeedsAccounts", "mcpNeedsAccounts"], ignoreOrder: true);
    }

    [Fact]
    public void APortalAlone_IsOfferedNoWebApp_ForTheWorkerAddedForIt()
    {
        var known = new Dictionary<string, string> { ["presentation"] = "blazor-server", ["auth"] = "external" };

        Offer offer = Offer.For(_project, Option(_project, "web"), known, Launcher.ProjectAskedWhen);

        offer.Values.ShouldContain("none");
        offer.Reasons.ShouldBeEmpty();
    }

    [Fact]
    public void APortalAlone_OnSqlite_IsRefusedTheWorkerAddedForIt()
    {
        var known = new Dictionary<string, string> { ["presentation"] = "blazor-server", ["auth"] = "external", ["web"] = "none", ["database"] = "sqlite" };

        Offer.Unavoidable(_project, known, Launcher.ProjectAskedWhen).Select(refusal => refusal.Symbol).ShouldBe(["workerNeedsServerDatabase"]);
        Offer.Unavoidable(_project, new Dictionary<string, string>(known) { ["presentation"] = "api|blazor-server" }, Launcher.ProjectAskedWhen).ShouldBeEmpty();
    }

    private List<string> Accepted(TemplateOption option, IReadOnlyList<string> offered, Dictionary<string, string> answers) =>
        [.. Enumerable.Range(1, (1 << offered.Count) - 1)
            .Select(members => option.Joined(offered.Where((_, index) => (members & (1 << index)) != 0)))
            .Where(selection => Offer.Unavoidable(_project, new Dictionary<string, string>(answers) { [option.Symbol] = selection }, Launcher.ProjectAskedWhen).Count == 0)];

    private static TemplateOption Option(TemplateManifest manifest, string symbol) =>
        manifest.Options.Single(option => option.Symbol == symbol);
}

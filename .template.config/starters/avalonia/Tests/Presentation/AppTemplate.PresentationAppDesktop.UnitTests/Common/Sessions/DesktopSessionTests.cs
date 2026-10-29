using System.Net;
using System.Web;
using AppTemplate.Client.Common.Sessions;
using AppTemplate.PresentationApp.Common.Sessions;
using AppTemplate.PresentationApp.UnitTests.TestSupport;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace AppTemplate.PresentationApp.UnitTests.Common.Sessions;

public sealed class DesktopSessionTests
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task SigningIn_RunsTheCodeFlowWithPkce_ThroughTheLoopback_AndHoldsTheTokens()
    {
        var provider = new StubProvider(StubProvider.Tokens("a1", "r1"));
        var browser = new ScriptedBrowser();
        using var host = PresentationHost.Build(Stub(provider), browser: browser);
        var session = host.Services.GetRequiredService<DesktopSession>();

        await session.SignInAsync(Cancellation);

        var authorize = HttpUtility.ParseQueryString(browser.Opened!.Query);
        authorize["response_type"].ShouldBe("code");
        authorize["client_id"].ShouldBe(PresentationHost.ClientId);
        authorize["code_challenge_method"].ShouldBe("S256");
        authorize["code_challenge"].ShouldNotBeNullOrEmpty();
        new Uri(authorize["redirect_uri"]!).Host.ShouldBe("127.0.0.1");
        (await browser.Page!).ShouldContain(LoopbackBrowser.ReturnToTheApp);

        var redeemed = HttpUtility.ParseQueryString(provider.TokenRequests.ShouldHaveSingleItem());
        redeemed["grant_type"].ShouldBe("authorization_code");
        redeemed["code"].ShouldBe("c1");
        redeemed["code_verifier"].ShouldNotBeNullOrEmpty();
        redeemed["redirect_uri"].ShouldBe(authorize["redirect_uri"]);

        session.SignedInAs.ShouldBe("Ada Lovelace");
        (await session.AccessTokenAsync(Cancellation)).ShouldBe("a1");
        host.Services.GetRequiredService<IAccessTokenSource>().ShouldBeSameAs(session);
    }

    [Fact]
    public async Task ASignInTheProviderRefuses_FailsWithItsError_AndHoldsNothing()
    {
        var provider = new StubProvider();
        var browser = new ScriptedBrowser(state => $"error=access_denied&error_description=Denied&state={state}");
        using var host = PresentationHost.Build(Stub(provider), browser: browser);
        var session = host.Services.GetRequiredService<DesktopSession>();

        var refused = await Should.ThrowAsync<SignInFailedException>(() => session.SignInAsync(Cancellation));

        refused.Message.ShouldBe("access_denied: Denied");
        provider.TokenRequests.ShouldBeEmpty();
        session.IsSignedIn.ShouldBeFalse();
        (await session.AccessTokenAsync(Cancellation)).ShouldBeNull();
    }

    [Fact]
    public async Task AnUnreachableProvider_FailsTheSignIn()
    {
        using var host = PresentationHost.Build(new StubApi(_ => throw new HttpRequestException("No route to the provider.")));
        var session = host.Services.GetRequiredService<DesktopSession>();

        var failed = await Should.ThrowAsync<SignInFailedException>(() => session.SignInAsync(Cancellation));

        failed.Message.ShouldContain("discovery document");
        session.IsSignedIn.ShouldBeFalse();
    }

    [Fact]
    public async Task AnAccessTokenAboutToExpire_IsRefreshed_WithTheLatestRefreshToken()
    {
        int aboutToExpire = (int)DesktopSession.RefreshAhead.TotalSeconds / 2;
        var provider = new StubProvider(
            StubProvider.Tokens("a1", "r1", aboutToExpire),
            StubProvider.Tokens("a2", "r2", aboutToExpire),
            StubProvider.Tokens("a3"));
        using var host = PresentationHost.Build(Stub(provider));
        var session = host.Services.GetRequiredService<DesktopSession>();
        await session.SignInAsync(Cancellation);

        (await session.AccessTokenAsync(Cancellation)).ShouldBe("a2");
        (await session.AccessTokenAsync(Cancellation)).ShouldBe("a3");
        (await session.AccessTokenAsync(Cancellation)).ShouldBe("a3");

        provider.TokenRequests.Skip(1).Select(request => HttpUtility.ParseQueryString(request))
            .Select(refresh => $"{refresh["grant_type"]} {refresh["refresh_token"]}")
            .ShouldBe(["refresh_token r1", "refresh_token r2"]);
    }

    [Fact]
    public async Task CallsWaitingOnARefresh_ShareIt()
    {
        var provider = new StubProvider(StubProvider.Tokens("a1", "r1", expiresIn: 0), StubProvider.Tokens("a2", "r2"));
        bool signedIn = false;
        using var refreshArrived = new ManualResetEventSlim();
        using var releaseRefresh = new ManualResetEventSlim();
        var api = new StubApi(request =>
        {
            if (signedIn && request.RequestUri!.AbsolutePath == StubProvider.TokenPath)
            {
                refreshArrived.Set();
                releaseRefresh.Wait(Cancellation);
            }

            return provider.Answer(request) ?? new HttpResponseMessage(HttpStatusCode.NotFound);
        });
        using var host = PresentationHost.Build(api);
        var session = host.Services.GetRequiredService<DesktopSession>();
        await session.SignInAsync(Cancellation);
        signedIn = true;

        var first = session.AccessTokenAsync(Cancellation).AsTask();
        refreshArrived.Wait(Cancellation);
        var second = session.AccessTokenAsync(Cancellation).AsTask();
        releaseRefresh.Set();

        (await first).ShouldBe("a2");
        (await second).ShouldBe("a2");
        provider.TokenRequests.Count.ShouldBe(2);
    }

    [Fact]
    public async Task WithoutARefreshToken_AnExpiredAccessToken_IsNoLongerOffered()
    {
        var provider = new StubProvider(StubProvider.Tokens("a1", refreshToken: null, expiresIn: 0));
        using var host = PresentationHost.Build(Stub(provider));
        var session = host.Services.GetRequiredService<DesktopSession>();
        await session.SignInAsync(Cancellation);

        (await session.AccessTokenAsync(Cancellation)).ShouldBeNull();

        session.IsSignedIn.ShouldBeFalse();
        provider.TokenRequests.Count.ShouldBe(1);
    }

    [Fact]
    public async Task ARefusedRefresh_EndsAnExpiredSession()
    {
        var provider = new StubProvider(StubProvider.Tokens("a1", "r1", expiresIn: 0), StubProvider.Refused("invalid_grant"));
        using var host = PresentationHost.Build(Stub(provider));
        var session = host.Services.GetRequiredService<DesktopSession>();
        await session.SignInAsync(Cancellation);

        (await session.AccessTokenAsync(Cancellation)).ShouldBeNull();

        session.IsSignedIn.ShouldBeFalse();
    }

    [Fact]
    public async Task SigningOut_ForgetsTheTokens()
    {
        var provider = new StubProvider(StubProvider.Tokens("a1", "r1"));
        using var host = PresentationHost.Build(Stub(provider));
        var session = host.Services.GetRequiredService<DesktopSession>();
        await session.SignInAsync(Cancellation);

        session.SignOut();

        session.IsSignedIn.ShouldBeFalse();
        session.SignedInAs.ShouldBeNull();
        (await session.AccessTokenAsync(Cancellation)).ShouldBeNull();
    }

    private static StubApi Stub(StubProvider provider) =>
        new(request => provider.Answer(request) ?? new HttpResponseMessage(HttpStatusCode.NotFound));
}

using System.Net;
using AppTemplate.Client.Common.Sessions;
using AppTemplate.Client.Generated.Models;
using AppTemplate.Client.UnitTests.TestSupport;
using Shouldly;
using Xunit;

namespace AppTemplate.Client.UnitTests.Common.Sessions;

public sealed class ApiSessionTests
{
    private const string _probe = "/api/v1/probe";
    private const string _login = "/api/v1/auth/login";
    private const string _refresh = "/api/v1/auth/refresh";
    private const string _logout = "/api/v1/auth/logout";
    private const string _unauthorized = """{"type":"about:blank","status":401,"code":"auth.unauthorized","traceId":"t"}""";

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ARequest_CarriesTheAccessTokenHeldInMemory()
    {
        var api = new StubApi(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)));
        using var harness = new ClientHarness(api);

        await harness.Session.SignedInAsync(new TokenResponse { AccessToken = "a1", RefreshToken = "r1" }, Cancellation);
        using var response = await harness.Http.GetAsync(_probe.TrimStart('/'), Cancellation);

        api.SentTo(_probe).Single().Authorization.ShouldBe("Bearer a1");
        (await harness.RefreshTokens.ReadAsync(Cancellation)).ShouldBe("r1");
    }

    [Fact]
    public async Task AnUnauthorizedAnswer_IsRefreshedOnce_AndReplayedWithTheNewToken()
    {
        var api = new StubApi(request => Task.FromResult(request switch
        {
            { Path: _refresh } => StubApi.Json(HttpStatusCode.OK, StubApi.Tokens("a2", "r2")),
            { Authorization: "Bearer a2" } => new HttpResponseMessage(HttpStatusCode.OK),
            _ => StubApi.Problem(HttpStatusCode.Unauthorized, _unauthorized),
        }));
        using var harness = new ClientHarness(api);
        await harness.Session.SignedInAsync(new TokenResponse { AccessToken = "a1", RefreshToken = "r1" }, Cancellation);

        using var response = await harness.Http.GetAsync(_probe.TrimStart('/'), Cancellation);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        api.SentTo(_probe).Select(request => request.Authorization).ShouldBe(["Bearer a1", "Bearer a2"]);
        var refresh = api.SentTo(_refresh).ShouldHaveSingleItem();
        refresh.Body.ShouldContain("\"refreshToken\":\"r1\"");
        refresh.RefreshTokenMode.ShouldBeNull();
        harness.Session.AccessToken.ShouldBe("a2");
        (await harness.RefreshTokens.ReadAsync(Cancellation)).ShouldBe("r2");
    }

    [Fact]
    public async Task AReplayedRequest_SendsItsBodyAgain()
    {
        var api = new StubApi(request => Task.FromResult(request switch
        {
            { Path: _refresh } => StubApi.Json(HttpStatusCode.OK, StubApi.Tokens("a2", "r2")),
            { Authorization: "Bearer a2" } => new HttpResponseMessage(HttpStatusCode.NoContent),
            _ => StubApi.Problem(HttpStatusCode.Unauthorized, _unauthorized),
        }));
        using var harness = new ClientHarness(api);
        await harness.Session.SignedInAsync(new TokenResponse { AccessToken = "a1", RefreshToken = "r1" }, Cancellation);

        await harness.Client.Api.V1.Auth.ChangePassword.PostAsync(
            new ChangePasswordRequest { CurrentPassword = "Before-1", NewPassword = "After-2" },
            cancellationToken: Cancellation);

        var sent = api.SentTo("/api/v1/auth/change-password").ToList();
        sent.Count.ShouldBe(2);
        sent[1].Body.ShouldBe(sent[0].Body);
        sent[1].Body.ShouldContain("After-2");
    }

    [Fact]
    public async Task ConcurrentRefreshes_ShareOneExchange()
    {
        var answer = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var api = new StubApi(_ => answer.Task);
        using var harness = new ClientHarness(api);
        await harness.Session.SignedInAsync(new TokenResponse { AccessToken = "a1", RefreshToken = "r1" }, Cancellation);

        var first = harness.Session.RefreshAsync(Cancellation);
        var second = harness.Session.RefreshAsync(Cancellation);
        answer.SetResult(StubApi.Json(HttpStatusCode.OK, StubApi.Tokens("a2", "r2")));

        (await Task.WhenAll(first, second)).ShouldBe([true, true]);
        api.SentTo(_refresh).ShouldHaveSingleItem();
        harness.Session.AccessToken.ShouldBe("a2");
    }

    [Fact]
    public async Task ConcurrentUnauthorizedRequests_RefreshOnce()
    {
        var bothRefused = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int refused = 0;
        var api = new StubApi(async request =>
        {
            switch (request)
            {
                case { Path: _refresh }:
                    await bothRefused.Task;
                    return StubApi.Json(HttpStatusCode.OK, StubApi.Tokens("a2", "r2"));
                case { Authorization: "Bearer a2" }:
                    return new HttpResponseMessage(HttpStatusCode.OK);
                default:
                    if (Interlocked.Increment(ref refused) == 2)
                    {
                        bothRefused.SetResult();
                    }

                    return StubApi.Problem(HttpStatusCode.Unauthorized, _unauthorized);
            }
        });
        using var harness = new ClientHarness(api);
        await harness.Session.SignedInAsync(new TokenResponse { AccessToken = "a1", RefreshToken = "r1" }, Cancellation);

        var responses = await Task.WhenAll(
            harness.Http.GetAsync(_probe.TrimStart('/'), Cancellation),
            harness.Http.GetAsync(_probe.TrimStart('/'), Cancellation));

        responses.Select(response => response.StatusCode).ShouldBe([HttpStatusCode.OK, HttpStatusCode.OK]);
        api.SentTo(_refresh).ShouldHaveSingleItem();

        foreach (var response in responses)
        {
            response.Dispose();
        }
    }

    [Fact]
    public async Task ARefusedRefresh_EndsTheSession_AndReturnsTheUnauthorizedAnswer()
    {
        var api = new StubApi(_ => Task.FromResult(StubApi.Problem(HttpStatusCode.Unauthorized, _unauthorized)));
        using var harness = new ClientHarness(api);
        await harness.Session.SignedInAsync(new TokenResponse { AccessToken = "a1", RefreshToken = "r1" }, Cancellation);

        using var response = await harness.Http.GetAsync(_probe.TrimStart('/'), Cancellation);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        api.SentTo(_refresh).ShouldHaveSingleItem();
        api.SentTo(_probe).ShouldHaveSingleItem();
        harness.Session.IsSignedIn.ShouldBeFalse();
        (await harness.RefreshTokens.ReadAsync(Cancellation)).ShouldBeNull();
    }

    [Fact(Timeout = 10_000)]
    public async Task ASessionRoute_IsNeverRefreshed()
    {
        var api = new StubApi(_ => Task.FromResult(StubApi.Problem(HttpStatusCode.Unauthorized, _unauthorized)));
        using var harness = new ClientHarness(api);
        await harness.Session.SignedInAsync(new TokenResponse { AccessToken = "a1", RefreshToken = "r1" }, TestContext.Current.CancellationToken);

        using var response = await harness.Http.PostAsync(_login.TrimStart('/'), new StringContent("{}"), TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        api.SentTo(_refresh).ShouldBeEmpty();
        api.SentTo(_login).Single().RefreshTokenMode.ShouldBeNull();
    }

    [Fact]
    public async Task WithoutARefreshToken_TheBodyModeAsksTheApiNothing()
    {
        var api = new StubApi(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)));
        using var harness = new ClientHarness(api);

        (await harness.Session.RefreshAsync(Cancellation)).ShouldBeFalse();

        api.Sent.ShouldBeEmpty();
    }

    [Fact]
    public async Task TheCookieMode_RestoresASessionFromTheCookie_WithTheModeHeaderAndNoToken()
    {
        var api = new StubApi(request => Task.FromResult(request switch
        {
            { Path: _refresh } => StubApi.Json(HttpStatusCode.OK, StubApi.Tokens("a2", refreshToken: null)),
            { Authorization: "Bearer a2" } => new HttpResponseMessage(HttpStatusCode.OK),
            _ => StubApi.Problem(HttpStatusCode.Unauthorized, _unauthorized),
        }));
        using var harness = new ClientHarness(api, new CookieRefreshTokenStore());

        using var response = await harness.Http.GetAsync(_probe.TrimStart('/'), Cancellation);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var refresh = api.SentTo(_refresh).ShouldHaveSingleItem();
        refresh.RefreshTokenMode.ShouldBe("cookie");
        refresh.Body.ShouldBeEmpty();
        api.SentTo(_probe).Select(request => request.Authorization).ShouldBe([null, "Bearer a2"]);
    }

    [Fact]
    public async Task TheCookieMode_AsksForTheCookieOnSignIn()
    {
        var api = new StubApi(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)));
        using var harness = new ClientHarness(api, new CookieRefreshTokenStore());

        using var signIn = await harness.Http.PostAsync(_login.TrimStart('/'), new StringContent("{}"), Cancellation);
        using var other = await harness.Http.GetAsync(_probe.TrimStart('/'), Cancellation);

        api.SentTo(_login).Single().RefreshTokenMode.ShouldBe("cookie");
        api.SentTo(_probe).Single().RefreshTokenMode.ShouldBeNull();
    }

    [Fact]
    public async Task SigningOut_ForgetsTheTokens_AndRevokesTheRefreshToken()
    {
        var api = new StubApi(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent)));
        using var harness = new ClientHarness(api);
        await harness.Session.SignedInAsync(new TokenResponse { AccessToken = "a1", RefreshToken = "r1" }, Cancellation);

        await harness.Session.SignOutAsync(Cancellation);

        harness.Session.IsSignedIn.ShouldBeFalse();
        (await harness.RefreshTokens.ReadAsync(Cancellation)).ShouldBeNull();
        var logout = api.SentTo(_logout).ShouldHaveSingleItem();
        logout.Body.ShouldContain("\"refreshToken\":\"r1\"");
        logout.Authorization.ShouldBeNull();
    }

    [Fact]
    public async Task SigningOut_InTheCookieMode_AsksTheApiToClearTheCookie()
    {
        var api = new StubApi(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent)));
        using var harness = new ClientHarness(api, new CookieRefreshTokenStore());
        await harness.Session.SignedInAsync(new TokenResponse { AccessToken = "a1" }, Cancellation);

        await harness.Session.SignOutAsync(Cancellation);

        harness.Session.IsSignedIn.ShouldBeFalse();
        var logout = api.SentTo(_logout).ShouldHaveSingleItem();
        logout.RefreshTokenMode.ShouldBe("cookie");
        logout.Body.ShouldBeEmpty();
    }

    [Fact]
    public async Task ASignInDuringARefresh_KeepsTheTokensOfTheSignIn()
    {
        var answer = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var api = new StubApi(_ => answer.Task);
        using var harness = new ClientHarness(api);
        await harness.Session.SignedInAsync(new TokenResponse { AccessToken = "a1", RefreshToken = "r1" }, Cancellation);

        var refresh = harness.Session.RefreshAsync(Cancellation);
        await harness.Session.SignedInAsync(new TokenResponse { AccessToken = "b1", RefreshToken = "s1" }, Cancellation);
        answer.SetResult(StubApi.Json(HttpStatusCode.OK, StubApi.Tokens("a2", "r2")));

        (await refresh).ShouldBeTrue();
        harness.Session.AccessToken.ShouldBe("b1");
        (await harness.RefreshTokens.ReadAsync(Cancellation)).ShouldBe("s1");
    }

    [Fact]
    public async Task AnEmptySignInAnswer_IsRefused()
    {
        var api = new StubApi(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)));
        using var harness = new ClientHarness(api);

        await Should.ThrowAsync<ArgumentException>(() =>
            harness.Session.SignedInAsync(new TokenResponse { AccessToken = string.Empty }, Cancellation));

        harness.Session.IsSignedIn.ShouldBeFalse();
    }
}

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using AppTemplate.Api.Features.Auth.Contracts.Requests;
using AppTemplate.Api.Features.Auth.Contracts.Responses;
using AppTemplate.Api.IntegrationTests.Auth;
using AppTemplate.Api.IntegrationTests.Infrastructure;
using Microsoft.Net.Http.Headers;
using Shouldly;
using Xunit;

namespace AppTemplate.Api.IntegrationTests.Security;

public sealed class RefreshTokenCookieTests(ApiFixture fixture) : IntegrationTestBase(fixture)
{
    private const string _modeHeader = "X-Refresh-Token-Mode";
    private const string _cookieName = "refresh_token";
    private const string _cookiePath = "/api/v1/auth";

    private const string _liveGrants = """
        SELECT count(*) FROM identity."RefreshTokens" WHERE "RevokedAt" IS NULL AND "ReplacedByTokenHash" IS NULL
        """;

    [Fact]
    public async Task SigningInInCookieMode_SetsAnHttpOnlySecureStrictCookieOnTheAuthPath()
    {
        var client = CreateClient();
        var user = await RegisterConfirmedUserAsync(client);

        using var response = await SendAsync(client, "login", new LoginRequest(user.Email, user.Password));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var cookie = RefreshCookieOf(response);
        cookie.Value.ToString().ShouldNotBeNullOrWhiteSpace();
        cookie.HttpOnly.ShouldBeTrue("a cookie script can read is the body token again, with extra steps.");
        cookie.Secure.ShouldBeTrue();
        cookie.SameSite.ShouldBe(SameSiteMode.Strict);
        cookie.Path.ToString().ShouldBe(_cookiePath, "the cookie goes to the auth routes and to nothing else.");
        cookie.Domain.HasValue.ShouldBeFalse("a Domain attribute would share the cookie with every subdomain.");

        using var body = await JsonAsync(response);
        var expiresAt = body.RootElement.GetProperty("tokens").GetProperty("refreshTokenExpiresAt").GetDateTimeOffset();

        cookie.Expires.ShouldNotBeNull();
        cookie.Expires.Value.ShouldBe(expiresAt, TimeSpan.FromSeconds(1), "the cookie lives exactly as long as the grant.");
    }

    [Fact]
    public async Task SigningInInCookieMode_LeavesTheRefreshTokenOutOfTheBody()
    {
        var client = CreateClient();
        var user = await RegisterConfirmedUserAsync(client);

        using var response = await SendAsync(client, "login", new LoginRequest(user.Email, user.Password));

        using var body = await JsonAsync(response);

        PropertyNames(body.RootElement.GetProperty("tokens")).ShouldBe(
            ["accessToken", "accessTokenExpiresAt", "refreshTokenExpiresAt"],
            "in cookie mode the refresh token must not be anywhere script can read it.");

        (await response.Content.ReadAsStringAsync(TestToken)).ShouldNotContain(RefreshCookieOf(response).Value.ToString());
    }

    [Fact]
    public async Task SigningInInBodyMode_AnswersWithTheFourTokenFields_AndSetsNoCookie()
    {
        var client = CreateClient();
        var user = await RegisterConfirmedUserAsync(client);

        using var response = await client.PostAsJsonAsync(
            $"{AuthRoute}/login",
            new LoginRequest(user.Email, user.Password),
            TestToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Headers.Contains(HeaderNames.SetCookie).ShouldBeFalse("a client that did not ask for a cookie gets none.");

        using var body = await JsonAsync(response);

        PropertyNames(body.RootElement.GetProperty("tokens")).ShouldBe(
            ["accessToken", "accessTokenExpiresAt", "refreshToken", "refreshTokenExpiresAt"]);
    }

    [Fact]
    public async Task RefreshingInBodyMode_AnswersWithTheFourTokenFields_AndSetsNoCookie()
    {
        var (client, _, session) = await SignInAsync();

        using var response = await client.PostAsJsonAsync(
            $"{AuthRoute}/refresh",
            new RefreshAccessTokenRequest(session.Tokens.RefreshToken),
            TestToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Headers.Contains(HeaderNames.SetCookie).ShouldBeFalse();

        using var body = await JsonAsync(response);

        PropertyNames(body.RootElement).ShouldBe(
            ["accessToken", "accessTokenExpiresAt", "refreshToken", "refreshTokenExpiresAt"]);
    }

    [Fact]
    public async Task RefreshingInCookieMode_RotatesTheCookie_AndKeepsTheTokenOutOfTheBody()
    {
        var (client, first) = await SignInWithCookieAsync();

        using var response = await SendAsync(client, "refresh", content: null, cookie: first);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var rotated = RefreshCookieOf(response);
        rotated.Value.ToString().ShouldNotBe(first);
        rotated.HttpOnly.ShouldBeTrue();
        rotated.Secure.ShouldBeTrue();
        rotated.SameSite.ShouldBe(SameSiteMode.Strict);
        rotated.Path.ToString().ShouldBe(_cookiePath);

        using var body = await JsonAsync(response);

        PropertyNames(body.RootElement).ShouldBe(["accessToken", "accessTokenExpiresAt", "refreshTokenExpiresAt"]);

        using var profile = new HttpRequestMessage(HttpMethod.Get, $"{AuthRoute}/me");
        profile.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            body.RootElement.GetProperty("accessToken").GetString());

        using var authorised = await client.SendAsync(profile, TestToken);
        authorised.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ACookieModeRefresh_WithoutTheHeader_IsRefused_AndSpendsNothing()
    {
        var (client, presented) = await SignInWithCookieAsync();

        using var forged = await SendAsync(client, "refresh", content: null, cookie: presented, withHeader: false);

        forged.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ApiJson.ReadProblemAsync(forged, TestToken)).Code.ShouldBe("request.validationFailed");
        forged.Headers.Contains(HeaderNames.SetCookie).ShouldBeFalse();

        using var genuine = await SendAsync(client, "refresh", content: null, cookie: presented);

        genuine.StatusCode.ShouldBe(
            HttpStatusCode.OK,
            "the cookie is read only beside the header, so the headerless request touched no grant.");
    }

    [Fact]
    public async Task ACookieModeRefresh_WithoutTheCookie_IsRefused()
    {
        var client = CreateClient();

        using var response = await SendAsync(client, "refresh", content: null, cookie: null);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await ApiJson.ReadProblemAsync(response, TestToken)).Code.ShouldBe("auth.refreshToken.invalid");
    }

    [Fact]
    public async Task ReplayingARotatedCookie_RevokesTheWholeFamily_AndClearsTheCookie()
    {
        var (client, first) = await SignInWithCookieAsync();
        string second = await RefreshWithCookieAsync(client, first);

        using var replay = await SendAsync(client, "refresh", content: null, cookie: first);

        replay.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await ApiJson.ReadProblemAsync(replay, TestToken)).Code.ShouldBe("auth.refreshToken.invalid");
        ShouldBeCleared(RefreshCookieOf(replay));

        using var afterReplay = await SendAsync(client, "refresh", content: null, cookie: second);

        afterReplay.StatusCode.ShouldBe(HttpStatusCode.Unauthorized, "the replay killed the token it had been rotated into.");

        int live = await Database.CountAsync(
            """SELECT count(*) FROM identity."RefreshTokens" WHERE "RevokedAt" IS NULL""",
            TestToken);
        live.ShouldBe(0);
    }

    [Fact]
    public async Task LoggingOutInCookieMode_ClearsTheCookie_AndEndsTheSession()
    {
        var (client, presented) = await SignInWithCookieAsync();

        using var loggedOut = await SendAsync(client, "logout", content: null, cookie: presented);

        loggedOut.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        ShouldBeCleared(RefreshCookieOf(loggedOut));

        (await Database.CountAsync(_liveGrants, TestToken)).ShouldBe(0);

        using var refreshed = await SendAsync(client, "refresh", content: null, cookie: presented);

        refreshed.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ACookieModeLogout_WithoutTheHeader_IsRefused_AndEndsNothing()
    {
        var (client, presented) = await SignInWithCookieAsync();

        using var forged = await SendAsync(client, "logout", content: null, cookie: presented, withHeader: false);

        forged.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        forged.Headers.Contains(HeaderNames.SetCookie).ShouldBeFalse();

        (await Database.CountAsync(_liveGrants, TestToken)).ShouldBe(1);
    }

    [Theory]
    [InlineData("Cookie")]
    [InlineData("body")]
    [InlineData("cookie, cookie")]
    public async Task AModeThatIsNotCookie_IsRefusedBeforeAnyoneSignsIn(string mode)
    {
        var client = CreateClient();
        var user = await RegisterConfirmedUserAsync(client);

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{AuthRoute}/login")
        {
            Content = JsonContent.Create(new LoginRequest(user.Email, user.Password)),
        };
        request.Headers.TryAddWithoutValidation(_modeHeader, mode);

        using var response = await client.SendAsync(request, TestToken);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ApiJson.ReadProblemAsync(response, TestToken)).Code.ShouldBe("request.validationFailed");
        response.Headers.Contains(HeaderNames.SetCookie).ShouldBeFalse();

        int issued = await Database.CountAsync("""SELECT count(*) FROM identity."RefreshTokens" """, TestToken);
        issued.ShouldBe(0, "a refused mode must refuse the sign-in, not fall back to answering in the body.");
    }

    [Fact]
    public async Task ATwoFactorSignIn_SetsTheCookieOnlyWhenTheChallengeIsRedeemed()
    {
        var (client, user, session) = await SignInAsync();
        var (sharedKey, _) = await TwoFactorTestSupport.EnableTwoFactorAsync(client, user, session, TestToken);

        var browser = CreateClient();

        using var challenged = await SendAsync(browser, "login", new LoginRequest(user.Email, user.Password));

        challenged.StatusCode.ShouldBe(HttpStatusCode.OK);
        challenged.Headers.Contains(HeaderNames.SetCookie).ShouldBeFalse("a challenge issues no grant to hold.");

        var challenge = (await ApiJson.ReadAsync<LoginResponse>(challenged, TestToken))
            .ShouldBeOfType<LoginResponse.TwoFactorRequired>();

        using var redeemed = await SendAsync(
            browser,
            "login/two-factor",
            new VerifyTwoFactorRequest(challenge.ChallengeToken, AuthenticatorCodes.CurrentCodeFor(sharedKey)));

        redeemed.StatusCode.ShouldBe(HttpStatusCode.OK);
        RefreshCookieOf(redeemed).Value.ToString().ShouldNotBeNullOrWhiteSpace();

        using var body = await JsonAsync(redeemed);

        PropertyNames(body.RootElement.GetProperty("tokens"))
            .ShouldBe(["accessToken", "accessTokenExpiresAt", "refreshTokenExpiresAt"]);
    }

    [Fact]
    public async Task NoLogEntry_CarriesTheCookiesValue()
    {
        var (client, first) = await SignInWithCookieAsync();
        string second = await RefreshWithCookieAsync(client, first);

        var written = Fixture.Logs.Snapshot();
        written.ShouldNotBeEmpty("Nothing was logged at all, so this search proves nothing.");

        foreach (string secret in new[] { first, second })
        {
            written.Where(record => record.Message.Contains(secret, StringComparison.Ordinal))
                .ShouldBeEmpty("A log entry carries a refresh token read from or written to the cookie.");
        }
    }

    [Fact]
    public async Task TheOpenApiDocument_DescribesTheCookieMode()
    {
        var client = CreateClient();

        using var response = await client.GetAsync(new Uri("/openapi/v1.json", UriKind.Relative), TestToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        using var document = await JsonAsync(response);
        var paths = document.RootElement.GetProperty("paths");

        string[] issuing = ["login", "login/two-factor", "login/external", "passkeys/sign-in", "refresh", "logout"];

        foreach (string route in issuing)
        {
            var operation = paths.GetProperty($"{AuthRoute}/{route}").GetProperty("post");
            var parameters = operation.GetProperty("parameters").EnumerateArray().ToList();

            var mode = parameters.Single(parameter => parameter.GetProperty("name").GetString() == _modeHeader);
            mode.GetProperty("in").GetString().ShouldBe("header");
            mode.GetProperty("description").GetString().ShouldNotBeNull().ShouldContain(_cookieName);

            var success = operation.GetProperty("responses").EnumerateObject().Single(answer => answer.Name.StartsWith('2'));
            success.Value.GetProperty("headers").TryGetProperty("Set-Cookie", out _)
                .ShouldBeTrue($"{route} does not say it sets the cookie.");

            bool readsCookie = parameters.Any(parameter =>
                parameter.GetProperty("in").GetString() == "cookie"
                && parameter.GetProperty("name").GetString() == _cookieName);

            readsCookie.ShouldBe(route is "refresh" or "logout", $"{route} misstates whether it reads the cookie.");
        }

        var tokenSchema = document.RootElement.GetProperty("components").GetProperty("schemas").GetProperty("TokenResponse");

        (tokenSchema.TryGetProperty("required", out var required)
            && required.EnumerateArray().Any(name => name.GetString() == "refreshToken"))
            .ShouldBeFalse("a generated client must not promise a refreshToken the cookie mode leaves out.");

        tokenSchema.GetProperty("properties").GetProperty("refreshToken").GetProperty("description").GetString()
            .ShouldNotBeNull()
            .ShouldContain(_cookieName);
    }

    private async Task<(HttpClient Client, string RefreshCookie)> SignInWithCookieAsync()
    {
        var client = CreateClient();
        var user = await RegisterConfirmedUserAsync(client);

        using var response = await SendAsync(client, "login", new LoginRequest(user.Email, user.Password));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        return (client, RefreshCookieOf(response).Value.ToString());
    }

    private static async Task<string> RefreshWithCookieAsync(HttpClient client, string presented)
    {
        using var response = await SendAsync(client, "refresh", content: null, cookie: presented);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        return RefreshCookieOf(response).Value.ToString();
    }

    private static Task<HttpResponseMessage> SendAsync<TBody>(HttpClient client, string route, TBody body) =>
        SendAsync(client, route, JsonContent.Create(body), cookie: null);

    private static async Task<HttpResponseMessage> SendAsync(
        HttpClient client,
        string route,
        HttpContent? content,
        string? cookie,
        bool withHeader = true)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{AuthRoute}/{route}") { Content = content };

        if (withHeader)
        {
            request.Headers.Add(_modeHeader, "cookie");
        }

        if (cookie is not null)
        {
            request.Headers.Add(HeaderNames.Cookie, $"{_cookieName}={cookie}");
        }

        return await client.SendAsync(request, TestToken);
    }

    private static SetCookieHeaderValue RefreshCookieOf(HttpResponseMessage response)
    {
        response.Headers.TryGetValues(HeaderNames.SetCookie, out var values).ShouldBeTrue("no cookie was set.");

        var cookie = SetCookieHeaderValue.ParseList([.. values!]).ShouldHaveSingleItem();
        cookie.Name.ToString().ShouldBe(_cookieName);

        return cookie;
    }

    private static void ShouldBeCleared(SetCookieHeaderValue cookie)
    {
        cookie.Value.ToString().ShouldBeEmpty();
        cookie.Expires.ShouldNotBeNull();
        cookie.Expires.Value.ShouldBeLessThan(DateTimeOffset.UtcNow, "an expiry in the past is what deletes a cookie.");
        cookie.Path.ToString().ShouldBe(_cookiePath, "a deletion on another path leaves the real cookie in place.");
        cookie.Secure.ShouldBeTrue();
        cookie.HttpOnly.ShouldBeTrue();
        cookie.SameSite.ShouldBe(SameSiteMode.Strict);
    }

    private static async Task<JsonDocument> JsonAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestToken));

    private static IReadOnlyList<string> PropertyNames(JsonElement element) =>
    [
        .. element.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal),
    ];
}

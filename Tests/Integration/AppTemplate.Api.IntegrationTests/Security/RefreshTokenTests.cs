using System.Buffers.Text;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using AppTemplate.Api.Features.Auth.Contracts.Requests;
using AppTemplate.Api.Features.Auth.Contracts.Responses;
using AppTemplate.Api.IntegrationTests.Infrastructure;
using Shouldly;
using Xunit;

namespace AppTemplate.Api.IntegrationTests.Security;

public sealed class RefreshTokenTests(ApiFixture fixture) : IntegrationTestBase(fixture)
{
    private const string _refreshTokenHashes = """
        SELECT "TokenHash" FROM identity."RefreshTokens" ORDER BY "CreatedAt"
        """;

    [Fact]
    public async Task ARefreshToken_CannotBeUsedAsAnAccessToken()
    {
        var (client, _, session) = await SignInAsync();

        using var authorised = await client.GetAsync(new Uri(ProtectedRoute, UriKind.Relative), TestToken);
        authorised.StatusCode.ShouldBe(HttpStatusCode.OK);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session.Tokens.RefreshToken);

        using var response = await client.GetAsync(new Uri(ProtectedRoute, UriKind.Relative), TestToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task AnAccessToken_CannotBeUsedAsARefreshToken()
    {
        var (client, _, session) = await SignInAsync();

        using var response = await client.PostAsJsonAsync(
            $"{AuthRoute}/refresh",
            new RefreshAccessTokenRequest(session.Tokens.AccessToken),
            TestToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await ApiJson.ReadProblemAsync(response, TestToken)).Code.ShouldBe("auth.refreshToken.invalid");
    }

    [Fact]
    public async Task TheTwoTokens_AreNotTheSameValue()
    {
        var (_, _, session) = await SignInAsync();

        session.Tokens.RefreshToken.ShouldNotBe(session.Tokens.AccessToken);
        session.Tokens.RefreshTokenExpiresAt.ShouldBeGreaterThan(session.Tokens.AccessTokenExpiresAt);
    }

    [Fact]
    public async Task Refreshing_IssuesANewPairAndConsumesThePresentedToken()
    {
        var (client, _, first) = await SignInAsync();

        using var refreshed = await client.PostAsJsonAsync(
            $"{AuthRoute}/refresh",
            new RefreshAccessTokenRequest(first.Tokens.RefreshToken),
            TestToken);

        refreshed.StatusCode.ShouldBe(HttpStatusCode.OK);

        var second = await ApiJson.ReadAsync<TokenResponse>(refreshed, TestToken);

        second.RefreshToken.ShouldNotBe(first.Tokens.RefreshToken);
        second.AccessToken.ShouldNotBeNullOrWhiteSpace();

        using var withNewAccessToken = new HttpRequestMessage(HttpMethod.Get, ProtectedRoute);
        withNewAccessToken.Headers.Authorization = new AuthenticationHeaderValue("Bearer", second.AccessToken);

        using var authorised = await client.SendAsync(withNewAccessToken, TestToken);
        authorised.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ReplayingAConsumedToken_FailsAndRevokesTheWholeFamily()
    {
        var (client, _, first) = await SignInAsync();

        var second = await RefreshAsync(client, first.Tokens.RefreshToken);

        var third = await RefreshAsync(client, second.RefreshToken);

        using var replay = await client.PostAsJsonAsync(
            $"{AuthRoute}/refresh",
            new RefreshAccessTokenRequest(first.Tokens.RefreshToken),
            TestToken);

        replay.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await ApiJson.ReadProblemAsync(replay, TestToken)).Code.ShouldBe("auth.refreshToken.invalid");

        using var afterReplay = await client.PostAsJsonAsync(
            $"{AuthRoute}/refresh",
            new RefreshAccessTokenRequest(third.RefreshToken),
            TestToken);

        afterReplay.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        int live = await Database.CountAsync(
            """SELECT count(*) FROM identity."RefreshTokens" WHERE "RevokedAt" IS NULL""",
            TestToken);
        live.ShouldBe(0);
    }

    [Fact]
    public async Task AnUnknownRefreshToken_IsRejectedIndistinguishablyFromAConsumedOne()
    {
        var (client, _, session) = await SignInAsync();

        var unknown = await ReadProblemForRefreshAsync(client, Base64Url.EncodeToString(new byte[32]));

        await RefreshAsync(client, session.Tokens.RefreshToken);
        var consumed = await ReadProblemForRefreshAsync(client, session.Tokens.RefreshToken);

        unknown.Status.ShouldBe(401);
        consumed.Status.ShouldBe(unknown.Status);
        consumed.Code.ShouldBe(unknown.Code);
        consumed.Title.ShouldBe(unknown.Title);
        consumed.Detail.ShouldBe(unknown.Detail);
    }

    [Fact]
    public async Task AfterLogout_TheRefreshTokenNoLongerRefreshes()
    {
        var (client, _, session) = await SignInAsync();

        using var loggedOut = await client.PostAsJsonAsync(
            $"{AuthRoute}/logout",
            new LogoutRequest(session.Tokens.RefreshToken),
            TestToken);

        loggedOut.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        using var refreshed = await client.PostAsJsonAsync(
            $"{AuthRoute}/refresh",
            new RefreshAccessTokenRequest(session.Tokens.RefreshToken),
            TestToken);

        refreshed.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await ApiJson.ReadProblemAsync(refreshed, TestToken)).Code.ShouldBe("auth.refreshToken.invalid");

        int live = await Database.CountAsync(
            """SELECT count(*) FROM identity."RefreshTokens" WHERE "RevokedAt" IS NULL""",
            TestToken);
        live.ShouldBe(0);
    }

    [Fact]
    public async Task LoggingOutWithAnUnknownToken_IsSilent()
    {
        var (client, _, _) = await SignInAsync();

        using var response = await client.PostAsJsonAsync(
            $"{AuthRoute}/logout",
            new LogoutRequest(Base64Url.EncodeToString(new byte[32])),
            TestToken);

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task LogoutEverywhere_RevokesTheRefreshTokensOfEverySession()
    {
        var setup = CreateClient();
        var user = await RegisterConfirmedUserAsync(setup);

        var deviceA = CreateClient();
        var tokensA = await LoginAsync(deviceA, user);
        deviceA.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokensA.AccessToken);

        var deviceB = CreateClient();
        var tokensB = await LoginAsync(deviceB, user);
        deviceB.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokensB.AccessToken);

        using var loggedOutAll = await deviceA.PostAsync(
            new Uri($"{AuthRoute}/logout-all", UriKind.Relative), content: null, TestToken);
        loggedOutAll.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        using var refreshA = await deviceA.PostAsJsonAsync(
            $"{AuthRoute}/refresh", new RefreshAccessTokenRequest(tokensA.RefreshToken), TestToken);
        refreshA.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        using var refreshB = await deviceB.PostAsJsonAsync(
            $"{AuthRoute}/refresh", new RefreshAccessTokenRequest(tokensB.RefreshToken), TestToken);
        refreshB.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task LogoutEverywhere_DoesNotInvalidateTheCallersOwnAccessToken()
    {
        var (client, _, _) = await SignInAsync();

        using var loggedOutAll = await client.PostAsync(
            new Uri($"{AuthRoute}/logout-all", UriKind.Relative), content: null, TestToken);
        loggedOutAll.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        using var stillAuthenticated = await client.GetAsync(new Uri(ProtectedRoute, UriKind.Relative), TestToken);
        stillAuthenticated.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task TheStoredRefreshToken_IsASha256HashAndNotTheTokenItself()
    {
        var (_, _, session) = await SignInAsync();

        var stored = await Database.QueryAsync(_refreshTokenHashes, TestToken);

        stored.Count.ShouldBe(1);

        string storedHash = stored[0].Trim();
        storedHash.ShouldNotBe(session.Tokens.RefreshToken);

        storedHash.ShouldBe(Base64Url.EncodeToString(
            SHA256.HashData(Encoding.UTF8.GetBytes(session.Tokens.RefreshToken))));
    }

    [Fact]
    public async Task TheRotationChain_IsRecordedByHashOnly()
    {
        var (client, _, first) = await SignInAsync();
        var second = await RefreshAsync(client, first.Tokens.RefreshToken);

        var stored = await Database.QueryAsync(_refreshTokenHashes, TestToken);

        stored.Count.ShouldBe(2);
        stored.Select(hash => hash.Trim()).ShouldNotContain(first.Tokens.RefreshToken);
        stored.Select(hash => hash.Trim()).ShouldNotContain(second.RefreshToken);

        var successors = await Database.QueryAsync(
            """
            SELECT "ReplacedByTokenHash" FROM identity."RefreshTokens" WHERE "ReplacedByTokenHash" IS NOT NULL
            """,
            TestToken);

        successors.Count.ShouldBe(1);
        successors[0].Trim().ShouldBe(Base64Url.EncodeToString(
            SHA256.HashData(Encoding.UTF8.GetBytes(second.RefreshToken))));
    }

    private static async Task<TokenResponse> RefreshAsync(HttpClient client, string refreshToken)
    {
        using var response = await client.PostAsJsonAsync(
            $"{AuthRoute}/refresh",
            new RefreshAccessTokenRequest(refreshToken),
            TestToken);

        if (response.StatusCode != HttpStatusCode.OK)
        {
            throw new InvalidOperationException(
                $"Refreshing failed with {(int)response.StatusCode}: " +
                await response.Content.ReadAsStringAsync(TestToken));
        }

        return await ApiJson.ReadAsync<TokenResponse>(response, TestToken);
    }

    private static async Task<ProblemResponse> ReadProblemForRefreshAsync(HttpClient client, string refreshToken)
    {
        using var response = await client.PostAsJsonAsync(
            $"{AuthRoute}/refresh",
            new RefreshAccessTokenRequest(refreshToken),
            TestToken);

        return await ApiJson.ReadProblemAsync(response, TestToken);
    }
}

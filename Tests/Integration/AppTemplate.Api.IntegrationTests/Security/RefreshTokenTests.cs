using System.Buffers.Text;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using AppTemplate.Api.IntegrationTests.Infrastructure;
using AppTemplate.Application.Features.Auth.Dtos;
using AppTemplate.Application.Features.Auth.UseCases.Commands;
using Shouldly;
using Xunit;

namespace AppTemplate.Api.IntegrationTests.Security;

/// <summary>
/// Everything the refresh token is supposed to be, and to not be.
/// </summary>
/// <remarks>
/// A refresh token minted like an access token — same signing key, issuer, audience and claim set —
/// would authenticate every protected endpoint if it leaked. The four regions below pin the four
/// properties that prevent that: not usable as a credential, rotated, revoked on logout, hashed at
/// rest.
/// </remarks>
public sealed class RefreshTokenTests(ApiFixture fixture) : IntegrationTestBase(fixture)
{
    private const string _refreshTokenHashes = """
        SELECT "TokenHash" FROM identity."RefreshTokens" ORDER BY "CreatedAt"
        """;

    #region A refresh token is not a credential for the API

    /// <summary>
    /// The one that matters most. Presenting the refresh token where an access token belongs must
    /// authenticate nothing.
    /// </summary>
    [Fact]
    public async Task ARefreshToken_CannotBeUsedAsAnAccessToken()
    {
        var (client, _, session) = await SignInAsync();

        // Sanity: with the access token this very request succeeds, so a 401 below can only be about
        // the token that was swapped in.
        using var authorised = await client.GetAsync(new Uri(TodoListsRoute, UriKind.Relative), TestToken);
        authorised.StatusCode.ShouldBe(HttpStatusCode.OK);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session.RefreshToken);

        using var response = await client.GetAsync(new Uri(TodoListsRoute, UriKind.Relative), TestToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    /// <summary>
    /// The mirror image, which is what makes the two tokens genuinely different kinds of thing
    /// rather than the same secret used in two places.
    /// </summary>
    [Fact]
    public async Task AnAccessToken_CannotBeUsedAsARefreshToken()
    {
        var (client, _, session) = await SignInAsync();

        using var response = await client.PostAsJsonAsync(
            $"{AuthRoute}/refresh",
            new RefreshAccessTokenRequest(session.AccessToken),
            TestToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await ApiJson.ReadProblemAsync(response, TestToken)).Code.ShouldBe("auth.refreshToken.invalid");
    }

    [Fact]
    public async Task TheTwoTokens_AreNotTheSameValue()
    {
        var (_, _, session) = await SignInAsync();

        session.RefreshToken.ShouldNotBe(session.AccessToken);
        session.RefreshTokenExpiresAt.ShouldBeGreaterThan(session.AccessTokenExpiresAt);
    }

    #endregion

    #region Rotation, and revocation of the whole family on reuse

    [Fact]
    public async Task Refreshing_IssuesANewPairAndConsumesThePresentedToken()
    {
        var (client, _, first) = await SignInAsync();

        using var refreshed = await client.PostAsJsonAsync(
            $"{AuthRoute}/refresh",
            new RefreshAccessTokenRequest(first.RefreshToken),
            TestToken);

        refreshed.StatusCode.ShouldBe(HttpStatusCode.OK);

        var second = await ApiJson.ReadAsync<RefreshAccessTokenResponse>(refreshed, TestToken);

        second.RefreshToken.ShouldNotBe(first.RefreshToken);
        second.AccessToken.ShouldNotBeNullOrWhiteSpace();

        // The replacement is a working credential, not just a different string.
        using var withNewAccessToken = new HttpRequestMessage(HttpMethod.Get, TodoListsRoute);
        withNewAccessToken.Headers.Authorization = new AuthenticationHeaderValue("Bearer", second.AccessToken);

        using var authorised = await client.SendAsync(withNewAccessToken, TestToken);
        authorised.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    /// <summary>
    /// Reuse detection. Presenting a token that has already been rotated is either a replay or a
    /// leak, so it fails <em>and</em> takes every other live grant for that user with it — otherwise
    /// whoever stole the token keeps the successor they were issued.
    /// </summary>
    [Fact]
    public async Task ReplayingAConsumedToken_FailsAndRevokesTheWholeFamily()
    {
        var (client, _, first) = await SignInAsync();

        var second = await RefreshAsync(client, first.RefreshToken);

        // Rotating once more establishes that the chain is working normally and leaves a live grant
        // behind, so anything that stops working below is the family revocation rather than a token
        // that was already dead.
        var third = await RefreshAsync(client, second.RefreshToken);

        // The replay.
        using var replay = await client.PostAsJsonAsync(
            $"{AuthRoute}/refresh",
            new RefreshAccessTokenRequest(first.RefreshToken),
            TestToken);

        replay.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await ApiJson.ReadProblemAsync(replay, TestToken)).Code.ShouldBe("auth.refreshToken.invalid");

        // The whole family is gone: the token that was live a moment ago no longer refreshes.
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

        await RefreshAsync(client, session.RefreshToken);
        var consumed = await ReadProblemForRefreshAsync(client, session.RefreshToken);

        unknown.Status.ShouldBe(401);
        consumed.Status.ShouldBe(unknown.Status);
        consumed.Code.ShouldBe(unknown.Code);
        consumed.Title.ShouldBe(unknown.Title);
        consumed.Detail.ShouldBe(unknown.Detail);
    }

    #endregion

    #region Logout revokes

    [Fact]
    public async Task AfterLogout_TheRefreshTokenNoLongerRefreshes()
    {
        var (client, _, session) = await SignInAsync();

        using var loggedOut = await client.PostAsJsonAsync(
            $"{AuthRoute}/logout",
            new LogoutRequest(session.RefreshToken),
            TestToken);

        loggedOut.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        using var refreshed = await client.PostAsJsonAsync(
            $"{AuthRoute}/refresh",
            new RefreshAccessTokenRequest(session.RefreshToken),
            TestToken);

        refreshed.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await ApiJson.ReadProblemAsync(refreshed, TestToken)).Code.ShouldBe("auth.refreshToken.invalid");

        int live = await Database.CountAsync(
            """SELECT count(*) FROM identity."RefreshTokens" WHERE "RevokedAt" IS NULL""",
            TestToken);
        live.ShouldBe(0);
    }

    /// <summary>
    /// Logging out with a token nobody issued must answer exactly as a successful logout does, or the
    /// endpoint becomes an oracle for guessing live tokens.
    /// </summary>
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

    #endregion

    #region Hashed at rest

    [Fact]
    public async Task TheStoredRefreshToken_IsASha256HashAndNotTheTokenItself()
    {
        var (_, _, session) = await SignInAsync();

        var stored = await Database.QueryAsync(_refreshTokenHashes, TestToken);

        stored.Count.ShouldBe(1);

        string storedHash = stored[0].Trim();
        storedHash.ShouldNotBe(session.RefreshToken);

        // Not merely "different": it is the base64url SHA-256 of the token that was handed out, so a
        // database disclosure yields nothing that can be presented to the refresh endpoint.
        storedHash.ShouldBe(Base64Url.EncodeToString(
            SHA256.HashData(Encoding.UTF8.GetBytes(session.RefreshToken))));
    }

    [Fact]
    public async Task TheRotationChain_IsRecordedByHashOnly()
    {
        var (client, _, first) = await SignInAsync();
        var second = await RefreshAsync(client, first.RefreshToken);

        var stored = await Database.QueryAsync(_refreshTokenHashes, TestToken);

        stored.Count.ShouldBe(2);
        stored.Select(hash => hash.Trim()).ShouldNotContain(first.RefreshToken);
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

    #endregion

    private static async Task<RefreshAccessTokenResponse> RefreshAsync(HttpClient client, string refreshToken)
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

        return await ApiJson.ReadAsync<RefreshAccessTokenResponse>(response, TestToken);
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

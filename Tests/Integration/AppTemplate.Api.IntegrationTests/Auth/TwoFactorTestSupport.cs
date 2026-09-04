using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using AppTemplate.Api.Features.Auth.Contracts.Requests;
using AppTemplate.Api.Features.Auth.Contracts.Responses;
using AppTemplate.Api.IntegrationTests.Infrastructure;
using Shouldly;

namespace AppTemplate.Api.IntegrationTests.Auth;

/// <summary>
/// The two-factor enrollment steps every test in this folder that needs an already-armed account
/// repeats: begin, refresh past the stamp rotation provisioning causes, confirm. Shared so that
/// <see cref="TwoFactorFlowTests"/> and <see cref="LoginResponseContractTests"/> do not each grow
/// their own slightly different copy.
/// </summary>
internal static class TwoFactorTestSupport
{
    /// <summary>Matches <c>IntegrationTestBase.AuthRoute</c>, which is <c>protected</c> to its own hierarchy.</summary>
    private const string _authRoute = "/api/v1/auth";

    public static async Task<SetUpTwoFactorResponse> BeginTwoFactorSetupAsync(HttpClient client, CancellationToken cancellationToken)
    {
        using var response = await client.PostAsync(
            new Uri($"{_authRoute}/two-factor/setup", UriKind.Relative),
            content: null,
            cancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        return await ApiJson.ReadAsync<SetUpTwoFactorResponse>(response, cancellationToken);
    }

    public static async Task<ConfirmTwoFactorSetupResponse> ConfirmTwoFactorSetupAsync(
        HttpClient client,
        string code,
        CancellationToken cancellationToken)
    {
        using var response = await client.PostAsJsonAsync(
            $"{_authRoute}/two-factor/confirm",
            new ConfirmTwoFactorSetupRequest(code),
            cancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        return await ApiJson.ReadAsync<ConfirmTwoFactorSetupResponse>(response, cancellationToken);
    }

    /// <summary>
    /// Provisioning a secret rotates the security stamp, which invalidates the access token that
    /// asked for it on its very next request — see <c>SetUpTwoFactorUseCase</c>. A real client's
    /// 401-retry would refresh; this does the same thing before continuing.
    /// </summary>
    public static async Task RefreshAuthorizationAsync(
        HttpClient client,
        string refreshToken,
        CancellationToken cancellationToken)
    {
        using var response = await client.PostAsJsonAsync(
            $"{_authRoute}/refresh",
            new RefreshAccessTokenRequest(refreshToken),
            cancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var tokens = await ApiJson.ReadAsync<TokenResponse>(response, cancellationToken);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
    }

    /// <summary>
    /// Enrolls and arms two-factor sign-in for the caller <paramref name="client"/> is authenticated
    /// as, from a fresh <see cref="TestSession"/>, and returns the shared key and the recovery codes.
    /// Confirming revokes every refresh token for the account, so <paramref name="client"/> carries no
    /// working credential once this returns — callers sign back in through the challenge flow.
    /// </summary>
    public static async Task<(string SharedKey, IReadOnlyList<string> RecoveryCodes)> EnableTwoFactorAsync(
        HttpClient client,
        TestSession session,
        CancellationToken cancellationToken)
    {
        var setup = await BeginTwoFactorSetupAsync(client, cancellationToken);
        await RefreshAuthorizationAsync(client, session.Tokens.RefreshToken, cancellationToken);
        var confirmed = await ConfirmTwoFactorSetupAsync(client, AuthenticatorCodes.CurrentCodeFor(setup.SharedKey), cancellationToken);

        return (setup.SharedKey, confirmed.RecoveryCodes);
    }

    /// <summary>A plain login that must stop at a challenge rather than a token pair.</summary>
    public static async Task<LoginResponse.TwoFactorRequired> LoginExpectingChallengeAsync(
        HttpClient client,
        TestUser user,
        CancellationToken cancellationToken)
    {
        using var response = await client.PostAsJsonAsync(
            $"{_authRoute}/login",
            new LoginRequest(user.Email, user.Password),
            cancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        return (await ApiJson.ReadAsync<LoginResponse>(response, cancellationToken))
            .ShouldBeOfType<LoginResponse.TwoFactorRequired>();
    }
}

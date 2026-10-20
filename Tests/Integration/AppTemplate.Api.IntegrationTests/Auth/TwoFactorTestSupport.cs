using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using AppTemplate.Api.Features.Auth.Contracts.Requests;
using AppTemplate.Api.Features.Auth.Contracts.Responses;
using AppTemplate.Api.IntegrationTests.Infrastructure;
using Shouldly;

namespace AppTemplate.Api.IntegrationTests.Auth;

internal static class TwoFactorTestSupport
{
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
        string currentPassword,
        string code,
        CancellationToken cancellationToken)
    {
        using var response = await client.PostAsJsonAsync(
            $"{_authRoute}/two-factor/confirm",
            new ConfirmTwoFactorSetupRequest(currentPassword, code),
            cancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        return await ApiJson.ReadAsync<ConfirmTwoFactorSetupResponse>(response, cancellationToken);
    }

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

    public static async Task<(string SharedKey, IReadOnlyList<string> RecoveryCodes)> EnableTwoFactorAsync(
        HttpClient client,
        TestUser user,
        TestSession session,
        CancellationToken cancellationToken)
    {
        var setup = await BeginTwoFactorSetupAsync(client, cancellationToken);
        await RefreshAuthorizationAsync(client, session.Tokens.RefreshToken, cancellationToken);
        var confirmed = await ConfirmTwoFactorSetupAsync(
            client, user.Password, AuthenticatorCodes.CurrentCodeFor(setup.SharedKey), cancellationToken);

        return (setup.SharedKey, confirmed.RecoveryCodes);
    }

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

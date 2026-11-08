using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AppTemplate.Api.Features.Auth.Contracts.Requests;
using AppTemplate.Api.Features.Auth.Contracts.Responses;
using AppTemplate.Api.IntegrationTests.Infrastructure;
using AppTemplate.Application.Auth.Features.ExternalLogins.Ports.ExternalIdentity;
using AppTemplate.Infrastructure.InMemory.Features.ExternalLogins;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace AppTemplate.Api.IntegrationTests.Auth;

internal static class PasskeyTestSupport
{
    public const string Origin = "https://client.integration.test";

    public const string RpId = "integration.test";

    private const string _authRoute = "/api/v1/auth";

    public static async Task<PasskeyRegistrationOptionsResponse> BeginRegistrationAsync(
        HttpClient client,
        CancellationToken cancellationToken)
    {
        using var response = await client.PostAsync(
            new Uri($"{_authRoute}/passkeys/register/options", UriKind.Relative),
            content: null,
            cancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(cancellationToken));

        return await ApiJson.ReadAsync<PasskeyRegistrationOptionsResponse>(response, cancellationToken);
    }

    public static Task<HttpResponseMessage> PostRegistrationAsync(
        HttpClient client,
        string credential,
        string registrationState,
        string? currentPassword,
        CancellationToken cancellationToken,
        string? name = "A key",
        string? originHeader = Origin) =>
        SendAsync(
            client,
            "passkeys/register",
            new CompletePasskeyRegistrationRequest(credential, registrationState, name, currentPassword),
            originHeader,
            cancellationToken);

    public static async Task<PasskeyResponse> RegisterAsync(
        HttpClient client,
        SoftwareAuthenticator authenticator,
        string? currentPassword,
        CancellationToken cancellationToken,
        string? name = "A key")
    {
        var options = await BeginRegistrationAsync(client, cancellationToken);

        using var response = await PostRegistrationAsync(
            client,
            authenticator.CreateCredential(options.CreationOptions, Origin),
            options.RegistrationState,
            currentPassword,
            cancellationToken,
            name);

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(cancellationToken));

        return await ApiJson.ReadAsync<PasskeyResponse>(response, cancellationToken);
    }

    public static async Task<PasskeySignInOptionsResponse> BeginSignInAsync(
        HttpClient client,
        CancellationToken cancellationToken)
    {
        using var response = await client.PostAsync(
            new Uri($"{_authRoute}/passkeys/sign-in/options", UriKind.Relative),
            content: null,
            cancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(cancellationToken));

        return await ApiJson.ReadAsync<PasskeySignInOptionsResponse>(response, cancellationToken);
    }

    public static Task<HttpResponseMessage> PostSignInAsync(
        HttpClient client,
        string credential,
        string authenticationState,
        CancellationToken cancellationToken,
        string? originHeader = Origin) =>
        SendAsync(
            client,
            "passkeys/sign-in",
            new PasskeySignInRequest(credential, authenticationState),
            originHeader,
            cancellationToken);

    public static async Task<LoginResponse.Authenticated> SignInAsync(
        HttpClient client,
        SoftwareAuthenticator authenticator,
        CancellationToken cancellationToken)
    {
        var options = await BeginSignInAsync(client, cancellationToken);

        using var response = await PostSignInAsync(
            client,
            authenticator.GetAssertion(options.SignInOptions, Origin),
            options.AuthenticationState,
            cancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(cancellationToken));

        return (await ApiJson.ReadAsync<LoginResponse>(response, cancellationToken))
            .ShouldBeOfType<LoginResponse.Authenticated>();
    }

    public static async Task<IReadOnlyList<PasskeyResponse>> ListAsync(
        HttpClient client,
        CancellationToken cancellationToken)
    {
        using var response = await client.GetAsync(new Uri($"{_authRoute}/passkeys", UriKind.Relative), cancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(cancellationToken));

        return (await ApiJson.ReadAsync<PasskeysResponse>(response, cancellationToken)).Passkeys;
    }

    public static async Task<uint> SignCountAsync(TestDatabase database, Guid userId, CancellationToken cancellationToken)
    {
        var counts = await database.QueryAsync(
            $"""SELECT "Data" FROM {DatabaseServer.Table("identity", "UserPasskeys")} WHERE "UserId" = {DatabaseServer.Literal(userId)}""",
            cancellationToken);

        using var data = JsonDocument.Parse(counts.ShouldHaveSingleItem());

        return data.RootElement.GetProperty("SignCount").GetUInt32();
    }

    public static PasskeyCeremonyFailures Failures(ApiFixture fixture) =>
        fixture.Factory.Services.GetRequiredService<PasskeyCeremonyFailures>();

    public static async Task<TokenResponse> SignInExternallyAsync(
        ApiFixture fixture,
        HttpClient client,
        CancellationToken cancellationToken)
    {
        const string provider = "google";
        string subject = $"passkey-{Guid.CreateVersion7():N}";
        string idToken = $"id-token-{subject}";

        fixture.Factory.Services.GetRequiredService<AcceptedExternalIdentities>().Accept(
            provider,
            idToken,
            new VerifiedExternalIdentity(provider, subject, $"{subject}@integration.test", EmailVerified: true));

        using var response = await client.PostAsJsonAsync(
            $"{_authRoute}/login/external",
            new SignInWithExternalProviderRequest(provider, idToken),
            cancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(cancellationToken));

        return (await ApiJson.ReadAsync<ExternalLoginResponse>(response, cancellationToken))
            .ShouldBeOfType<ExternalLoginResponse.Authenticated>().Tokens;
    }

    private static Task<HttpResponseMessage> SendAsync<TRequest>(
        HttpClient client,
        string route,
        TRequest body,
        string? originHeader,
        CancellationToken cancellationToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"{_authRoute}/{route}")
        {
            Content = JsonContent.Create(body),
        };

        if (originHeader is not null)
        {
            request.Headers.Add("Origin", originHeader);
        }

        return client.SendAsync(request, cancellationToken);
    }
}

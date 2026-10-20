using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AppTemplate.Api.Features.Auth.Contracts.Requests;
using AppTemplate.Api.IntegrationTests.Infrastructure;
using Shouldly;
using Xunit;

namespace AppTemplate.Api.IntegrationTests.Security;

public sealed class RequestLogRedactionTests(ApiFixture fixture) : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task TheAuthenticationFlow_WritesNoCredentialToAnyLog()
    {
        var (client, user, session) = await SignInAsync();

        using var listed = await client.GetAsync(new Uri(ProtectedRoute, UriKind.Relative), TestToken);
        listed.StatusCode.ShouldBe(HttpStatusCode.OK);

        using var refreshed = await client.PostAsJsonAsync(
            $"{AuthRoute}/refresh",
            new RefreshAccessTokenRequest(session.Tokens.RefreshToken),
            TestToken);
        refreshed.StatusCode.ShouldBe(HttpStatusCode.OK);

        using var rejected = await client.PostAsJsonAsync(
            $"{AuthRoute}/login",
            new LoginRequest(user.Email, "Not-The-Password-1!"),
            TestToken);
        rejected.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        var written = Fixture.Logs.Snapshot();

        written.ShouldNotBeEmpty("Nothing was logged at all, so this search proves nothing.");

        string[] secrets =
        [
            user.Password,
            "Not-The-Password-1!",
            session.Tokens.AccessToken,
            session.Tokens.RefreshToken,
        ];

        foreach (string secret in secrets)
        {
            secret.ShouldNotBeNullOrWhiteSpace();

            written.Where(record => record.Message.Contains(secret, StringComparison.Ordinal))
                .ShouldBeEmpty($"A log entry carries the secret '{Abbreviate(secret)}'.");
        }
    }

    [Fact]
    public async Task NoLogEntry_NamesTheAuthorizationHeaderOrACookie()
    {
        var (client, _, _) = await SignInAsync();

        using var response = await client.GetAsync(new Uri(ProtectedRoute, UriKind.Relative), TestToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var written = Fixture.Logs.Snapshot();
        written.ShouldNotBeEmpty();

        foreach (string name in new[] { "Authorization", "Cookie", "Set-Cookie" })
        {
            written.Where(record => record.Message.Contains(name, StringComparison.OrdinalIgnoreCase))
                .ShouldBeEmpty($"A log entry mentions '{name}'.");
        }
    }

    [Fact]
    public async Task TheRequestLog_CarriesTheTraceIdentifierFromTheProblemDetails()
    {
        var client = CreateClient();

        using var response = await client.GetAsync(new Uri(ProtectedRoute, UriKind.Relative), TestToken);
        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestToken));

        string? traceId = document.RootElement.GetProperty("traceId").GetString();
        traceId.ShouldNotBeNullOrWhiteSpace();

        Fixture.Logs.Snapshot()
            .Where(record => record.Message.Contains(traceId, StringComparison.Ordinal))
            .ShouldNotBeEmpty(
                $"No log entry carries the traceId '{traceId}' that the response handed the caller, " +
                "so the identifier in the response cannot be looked up.");
    }

    [Fact]
    public async Task TheLivenessProbe_IsNotLogged()
    {
        var client = CreateClient();

        using var response = await client.GetAsync(new Uri("/health", UriKind.Relative), TestToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        Fixture.Logs.Snapshot()
            .Where(record => record.Message.Contains("/health", StringComparison.Ordinal))
            .ShouldBeEmpty();
    }

    private static string Abbreviate(string secret) =>
        secret.Length <= 8 ? secret : $"{secret[..4]}…{secret[^4..]}";
}

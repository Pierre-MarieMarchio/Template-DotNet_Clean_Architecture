using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AppTemplate.Api.Features.Auth.Contracts.Requests;
using AppTemplate.Api.IntegrationTests.Infrastructure;
using Shouldly;
using Xunit;

namespace AppTemplate.Api.IntegrationTests.Security;

/// <summary>
/// Request logging must be incapable of writing a credential.
/// </summary>
/// <remarks>
/// This is the one place in the API where the temptation is structural: header and body logging is
/// three lines away and would be immediately useful, and the endpoints exercised below carry a
/// password, an access token and a refresh token — two of them in a JSON body, where "don't log
/// bodies" is the only workable rule. The test drives the whole authentication flow and then searches
/// every log entry the host wrote for those exact secret values.
/// </remarks>
public sealed class RequestLogRedactionTests(ApiFixture fixture) : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task TheAuthenticationFlow_WritesNoCredentialToAnyLog()
    {
        var (client, user, session) = await SignInAsync();

        // An authenticated request, so an Authorization header is on the wire.
        using var listed = await client.GetAsync(new Uri(TodoListsRoute, UriKind.Relative), TestToken);
        listed.StatusCode.ShouldBe(HttpStatusCode.OK);

        // A refresh, so a refresh token travels in a JSON body.
        using var refreshed = await client.PostAsJsonAsync(
            $"{AuthRoute}/refresh",
            new RefreshAccessTokenRequest(session.Tokens.RefreshToken),
            TestToken);
        refreshed.StatusCode.ShouldBe(HttpStatusCode.OK);

        // A failed login, so a password travels in a JSON body on a path that also logs a failure.
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

    /// <summary>
    /// The header name as well as its value: an entry reading <c>Authorization: [present]</c> is the
    /// first step towards one that reads <c>Authorization: Bearer ey…</c>.
    /// </summary>
    [Fact]
    public async Task NoLogEntry_NamesTheAuthorizationHeaderOrACookie()
    {
        var (client, _, _) = await SignInAsync();

        using var response = await client.GetAsync(new Uri(TodoListsRoute, UriKind.Relative), TestToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var written = Fixture.Logs.Snapshot();
        written.ShouldNotBeEmpty();

        foreach (string name in new[] { "Authorization", "Cookie", "Set-Cookie" })
        {
            written.Where(record => record.Message.Contains(name, StringComparison.OrdinalIgnoreCase))
                .ShouldBeEmpty($"A log entry mentions '{name}'.");
        }
    }

    /// <summary>
    /// The other half of the guarantee: the entry that <em>is</em> written carries what an
    /// investigation needs, so nobody has a reason to add the headers back.
    /// </summary>
    [Fact]
    public async Task TheRequestLog_CarriesTheTraceIdentifierFromTheProblemDetails()
    {
        var client = CreateClient();

        using var response = await client.GetAsync(new Uri(TodoListsRoute, UriKind.Relative), TestToken);
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

    /// <summary>Health probes are excluded, or they would be most of the log.</summary>
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

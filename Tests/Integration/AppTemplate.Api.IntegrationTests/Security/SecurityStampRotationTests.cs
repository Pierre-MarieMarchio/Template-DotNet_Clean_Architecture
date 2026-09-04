using System.Net;
using System.Net.Http.Json;
using AppTemplate.Api.Features.Auth.Contracts.Requests;
using AppTemplate.Api.IntegrationTests.Infrastructure;
using Shouldly;
using Xunit;

namespace AppTemplate.Api.IntegrationTests.Security;

/// <summary>
/// The security stamp is what lets a password change take effect before the access token it
/// invalidates would otherwise expire. Nothing else in the suite drives an access token through a
/// rotation, so this is the one place that guarantee is actually exercised rather than assumed.
/// </summary>
public sealed class SecurityStampRotationTests(ApiFixture fixture) : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task ChangingPassword_InvalidatesTheAccessTokenAlreadyInCirculation()
    {
        var (client, user, _) = await SignInAsync();

        // Sanity: the token works before anything happens to it. Without this, the 401 below could
        // just as well mean the token never worked at all.
        using var before = await client.GetAsync(new Uri($"{AuthRoute}/me", UriKind.Relative), TestToken);
        before.StatusCode.ShouldBe(HttpStatusCode.OK);

        using var changed = await client.PostAsJsonAsync(
            $"{AuthRoute}/change-password",
            new ChangePasswordRequest(user.Password, "Rotated!Password2"),
            TestToken);
        changed.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        // The exact same token, already issued, replayed unchanged.
        using var after = await client.GetAsync(new Uri($"{AuthRoute}/me", UriKind.Relative), TestToken);
        after.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        // A 401 alone proves nothing: expired, unsigned and revoked tokens all answer the same way to
        // a caller. The product hides which one happened on purpose — the test host does not, so the
        // reason is checked here instead of assumed.
        after.Headers.GetValues(ApiFactory.AuthFailureHeader)
            .ShouldContain("This token's security stamp is no longer valid.");
    }
}

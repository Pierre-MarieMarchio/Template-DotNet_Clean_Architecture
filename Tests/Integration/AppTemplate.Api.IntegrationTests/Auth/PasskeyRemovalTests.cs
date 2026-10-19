using System.Buffers.Text;
using System.Net;
using System.Net.Http.Json;
using AppTemplate.Api.Features.Auth.Contracts.Requests;
using AppTemplate.Api.IntegrationTests.Infrastructure;
using AppTemplate.Infrastructure.Auth.Features.Accounts.Rows.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace AppTemplate.Api.IntegrationTests.Auth;

/// <summary>
/// Removing a passkey against the real endpoint and the real database. The passkey is seeded
/// through Identity's own store rather than registered through a ceremony: the WebAuthn arithmetic
/// is the framework's, and what this proves is what the removal does to the sessions — the same as
/// a password change: every other session ends, the caller's own access token dies with the
/// security stamp, and the caller's own grant refreshes into the new posture.
/// </summary>
public sealed class PasskeyRemovalTests(ApiFixture fixture) : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task RemovingAPasskey_EndsEveryOtherSessionButSparesTheCallersOwn()
    {
        var (client, user, session) = await SignInAsync();

        var otherDevice = CreateClient();
        var otherTokens = await LoginAsync(otherDevice, user);

        byte[] credentialId = [1, 2, 3, 4, 5, 6, 7, 8];
        await SeedPasskeyAsync(session.UserId, credentialId);

        using var removed = await client.DeleteAsync(
            new Uri($"{AuthRoute}/passkeys/{Base64Url.EncodeToString(credentialId)}", UriKind.Relative),
            TestToken);
        removed.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        // The exact same access token, replayed unchanged: the stamp rotated under it.
        using var after = await client.GetAsync(new Uri($"{AuthRoute}/me", UriKind.Relative), TestToken);
        after.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        after.Headers.GetValues(ApiFactory.AuthFailureHeader)
            .ShouldContain("This token's security stamp is no longer valid.");

        using var refreshed = await client.PostAsJsonAsync(
            $"{AuthRoute}/refresh",
            new RefreshAccessTokenRequest(session.Tokens.RefreshToken),
            TestToken);
        refreshed.StatusCode.ShouldBe(HttpStatusCode.OK);

        using var otherRefreshed = await otherDevice.PostAsJsonAsync(
            $"{AuthRoute}/refresh",
            new RefreshAccessTokenRequest(otherTokens.RefreshToken),
            TestToken);
        otherRefreshed.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await ApiJson.ReadProblemAsync(otherRefreshed, TestToken)).Code.ShouldBe("auth.refreshToken.invalid");
    }

    /// <summary>A removal the store refuses — a credential the account does not hold — ends nothing.</summary>
    [Fact]
    public async Task RemovingAPasskeyTheAccountDoesNotHold_EndsNoSession()
    {
        var (client, user, _) = await SignInAsync();

        var otherDevice = CreateClient();
        var otherTokens = await LoginAsync(otherDevice, user);

        using var removed = await client.DeleteAsync(
            new Uri($"{AuthRoute}/passkeys/{Base64Url.EncodeToString([9, 9, 9])}", UriKind.Relative),
            TestToken);
        removed.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        using var after = await client.GetAsync(new Uri($"{AuthRoute}/me", UriKind.Relative), TestToken);
        after.StatusCode.ShouldBe(HttpStatusCode.OK);

        using var otherRefreshed = await otherDevice.PostAsJsonAsync(
            $"{AuthRoute}/refresh",
            new RefreshAccessTokenRequest(otherTokens.RefreshToken),
            TestToken);
        otherRefreshed.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    private async Task SeedPasskeyAsync(Guid userId, byte[] credentialId)
    {
        await using var scope = Fixture.Factory.Services.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var user = await userManager.FindByIdAsync(userId.ToString())
            ?? throw new InvalidOperationException($"No account has the id '{userId}'.");

        var added = await userManager.AddOrUpdatePasskeyAsync(user, new UserPasskeyInfo(
            credentialId,
            publicKey: [4, 5, 6],
            createdAt: Clock.UtcNow,
            signCount: 1,
            transports: [],
            isUserVerified: true,
            isBackupEligible: false,
            isBackedUp: false,
            attestationObject: [],
            clientDataJson: [])
        {
            Name = "YubiKey 5",
        });

        added.Succeeded.ShouldBeTrue();
    }
}

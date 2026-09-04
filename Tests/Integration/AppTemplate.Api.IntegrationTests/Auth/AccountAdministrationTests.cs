using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using AppTemplate.Api.Features.Auth.Contracts.Requests;
using AppTemplate.Api.Features.Auth.Contracts.Responses;
using AppTemplate.Api.IntegrationTests.Infrastructure;
using AppTemplate.Application.Features.Auth.Ports.RoleAssignments;
using AppTemplate.Infrastructure.Persistence.Common.Contexts;
using AppTemplate.Infrastructure.Persistence.Features.Identity;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace AppTemplate.Api.IntegrationTests.Auth;

/// <summary>
/// Locking, unlocking, granting a role, revoking a role, deleting an account outright — every one of
/// them restricted to <c>Policies.Administrator</c>, and three of them (lock, grant, revoke) proved
/// here to rotate the security stamp: <c>SetLockoutEndDateAsync</c>, <c>AddToRoleAsync</c> and
/// <c>RemoveFromRoleAsync</c> do not do that on their own, so an access token issued just before one
/// of these calls would otherwise keep validating for as long as it has left to live.
/// </summary>
/// <remarks>
/// This test host seeds no identity data at all (<c>IdentitySeed:Enabled</c> is <c>false</c> for the
/// whole suite — see <c>ApiFactory</c> and <c>AdministratorPolicyTests</c>), and no HTTP route can
/// grant the very first Administrator when none exists yet. <see cref="GrantRoleAsync"/> bootstraps
/// one directly through <see cref="IRoleAssignments"/> instead, inside a scope of the caller's own —
/// the same convention <c>LoadTodoListAsync</c> uses to read an aggregate back through the
/// application port rather than a persistence internal.
/// </remarks>
public sealed class AccountAdministrationTests(ApiFixture fixture) : IntegrationTestBase(fixture)
{
    private const string _accountsRoute = $"{AuthRoute}/accounts";

    [Fact]
    public async Task AnOrdinaryAuthenticatedUser_CannotLockAnotherAccount()
    {
        var (client, _, _) = await SignInAsync();
        var (_, _, target) = await SignInAsync();

        using var response = await client.PostAsync(
            new Uri($"{_accountsRoute}/{target.UserId}/lockout", UriKind.Relative), content: null, TestToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task AnUnauthenticatedCaller_Gets401()
    {
        var client = CreateClient();

        using var response = await client.PostAsync(
            new Uri($"{_accountsRoute}/{Guid.NewGuid()}/lockout", UriKind.Relative), content: null, TestToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    /// <summary>
    /// The gap this endpoint exists to close. Without the stamp rotation inside
    /// <c>AccountLockouts.LockAsync</c>, this test's final read answers 200, not 401.
    /// </summary>
    [Fact]
    public async Task LockingAnAccount_InvalidatesTheAccessTokenAlreadyInCirculation()
    {
        var (adminClient, _, _) = await SignInAsAdministratorAsync();
        var (targetClient, _, target) = await SignInAsync();

        using var before = await targetClient.GetAsync(new Uri($"{AuthRoute}/me", UriKind.Relative), TestToken);
        before.StatusCode.ShouldBe(HttpStatusCode.OK);

        using var locked = await adminClient.PostAsync(
            new Uri($"{_accountsRoute}/{target.UserId}/lockout", UriKind.Relative), content: null, TestToken);
        locked.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        // The exact same token, already issued, replayed unchanged.
        using var after = await targetClient.GetAsync(new Uri($"{AuthRoute}/me", UriKind.Relative), TestToken);
        after.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        after.Headers.GetValues(ApiFactory.AuthFailureHeader)
            .ShouldContain("This token's security stamp is no longer valid.");
    }

    [Fact]
    public async Task ALockedAccount_CannotSignInUntilUnlocked()
    {
        var (adminClient, _, _) = await SignInAsAdministratorAsync();
        var (_, targetUser, target) = await SignInAsync();

        await adminClient.PostAsync(
            new Uri($"{_accountsRoute}/{target.UserId}/lockout", UriKind.Relative), content: null, TestToken);

        using var loginWhileLocked = await CreateClient().PostAsJsonAsync(
            $"{AuthRoute}/login", new LoginRequest(targetUser.Email, targetUser.Password), TestToken);
        loginWhileLocked.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        using var unlocked = await adminClient.DeleteAsync(
            new Uri($"{_accountsRoute}/{target.UserId}/lockout", UriKind.Relative), TestToken);
        unlocked.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        using var loginAfterUnlock = await CreateClient().PostAsJsonAsync(
            $"{AuthRoute}/login", new LoginRequest(targetUser.Email, targetUser.Password), TestToken);
        loginAfterUnlock.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    /// <summary>
    /// Locking rotates the security stamp, which would invalidate the very token making this call —
    /// an administrator could otherwise strand every session but the one that just locked itself out.
    /// </summary>
    [Fact]
    public async Task AnAdministrator_CannotLockTheirOwnAccount()
    {
        var (adminClient, _, admin) = await SignInAsAdministratorAsync();

        using var response = await adminClient.PostAsync(
            new Uri($"{_accountsRoute}/{admin.UserId}/lockout", UriKind.Relative), content: null, TestToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task LockingAnUnknownAccount_Is404()
    {
        var (adminClient, _, _) = await SignInAsAdministratorAsync();

        using var response = await adminClient.PostAsync(
            new Uri($"{_accountsRoute}/{Guid.NewGuid()}/lockout", UriKind.Relative), content: null, TestToken);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    /// <summary>
    /// The gap this endpoint exists to close, mirroring
    /// <see cref="LockingAnAccount_InvalidatesTheAccessTokenAlreadyInCirculation"/>: without the
    /// rotation inside <c>RoleAssignments.RemoveRoleAsync</c>, the revoked role keeps authorising
    /// whatever it granted for as long as this token has left to live.
    /// </summary>
    [Fact]
    public async Task RevokingARole_InvalidatesTheAccessTokenAlreadyInCirculation()
    {
        var (adminClient, _, _) = await SignInAsAdministratorAsync();
        var (targetClient, targetUser, target) = await SignInAsync();

        await GrantRoleAsync(target.UserId, IdentityRoles.Administrator);

        // The token captured by SignInAsync predates the grant; a fresh one is what actually carries
        // the role claim the revocation below needs to invalidate.
        var tokensWithRole = await LoginAsync(targetClient, targetUser);
        targetClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", tokensWithRole.AccessToken);

        using var before = await targetClient.GetAsync(new Uri($"{AuthRoute}/me", UriKind.Relative), TestToken);
        before.StatusCode.ShouldBe(HttpStatusCode.OK);

        using var revoked = await adminClient.DeleteAsync(
            new Uri($"{_accountsRoute}/{target.UserId}/roles/{IdentityRoles.Administrator}", UriKind.Relative),
            TestToken);
        revoked.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        using var after = await targetClient.GetAsync(new Uri($"{AuthRoute}/me", UriKind.Relative), TestToken);
        after.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        after.Headers.GetValues(ApiFactory.AuthFailureHeader)
            .ShouldContain("This token's security stamp is no longer valid.");
    }

    /// <summary>
    /// Not narrowed to "may not remove their own Administrator role": <c>RemoveRoleUseCase</c> has no
    /// reference to that literal, so the guard refuses removing any role from the caller's own
    /// account. Proved here with the same role a real administrator would otherwise be blocked from
    /// removing from themselves — see <c>RemoveRoleUseCaseTests</c> for the unit-level proof that a
    /// different role is refused the same way.
    /// </summary>
    [Fact]
    public async Task AnAdministrator_CannotRemoveARoleFromTheirOwnAccount()
    {
        var (adminClient, _, admin) = await SignInAsAdministratorAsync();

        using var response = await adminClient.DeleteAsync(
            new Uri($"{_accountsRoute}/{admin.UserId}/roles/{IdentityRoles.Administrator}", UriKind.Relative),
            TestToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task GrantingAnUnseededRole_IsRejectedWithA400()
    {
        var (adminClient, _, _) = await SignInAsAdministratorAsync();
        var (_, _, target) = await SignInAsync();

        using var response = await adminClient.PutAsync(
            new Uri($"{_accountsRoute}/{target.UserId}/roles/NoSuchRole", UriKind.Relative), content: null, TestToken);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    /// <summary>
    /// Deleting is a more permanent version of the self-lockout <see cref="AnAdministrator_CannotLockTheirOwnAccount"/>
    /// refuses: nothing survives to undo it.
    /// </summary>
    [Fact]
    public async Task AnAdministrator_CannotDeleteTheirOwnAccount()
    {
        var (adminClient, _, admin) = await SignInAsAdministratorAsync();

        using var response = await adminClient.DeleteAsync(
            new Uri($"{_accountsRoute}/{admin.UserId}", UriKind.Relative), TestToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    /// <summary>
    /// No stamp to rotate here — see <c>IAccountDeletion</c> — because the row the stamp lived on is
    /// simply gone: the bearer handler looks the account up by id before it ever compares a stamp,
    /// and an id that no longer resolves fails there regardless.
    /// </summary>
    [Fact]
    public async Task DeletingAnAccount_InvalidatesItsAccessTokenImmediately()
    {
        var (adminClient, _, _) = await SignInAsAdministratorAsync();
        var (targetClient, _, target) = await SignInAsync();

        using var before = await targetClient.GetAsync(new Uri($"{AuthRoute}/me", UriKind.Relative), TestToken);
        before.StatusCode.ShouldBe(HttpStatusCode.OK);

        using var deleted = await adminClient.DeleteAsync(
            new Uri($"{_accountsRoute}/{target.UserId}", UriKind.Relative), TestToken);
        deleted.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        using var after = await targetClient.GetAsync(new Uri($"{AuthRoute}/me", UriKind.Relative), TestToken);
        after.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task DeletingAnUnknownAccount_Is404()
    {
        var (adminClient, _, _) = await SignInAsAdministratorAsync();

        using var response = await adminClient.DeleteAsync(
            new Uri($"{_accountsRoute}/{Guid.NewGuid()}", UriKind.Relative), TestToken);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task AnOrdinaryAuthenticatedUser_CannotDisableTwoFactorOnAnotherAccount()
    {
        var (client, _, _) = await SignInAsync();
        var (_, _, target) = await SignInAsync();

        using var response = await client.DeleteAsync(
            new Uri($"{_accountsRoute}/{target.UserId}/two-factor", UriKind.Relative), TestToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    /// <summary>
    /// Not the self-lockout reason <see cref="AnAdministrator_CannotLockTheirOwnAccount"/> gives —
    /// this account is fully reachable afterward either way. It is refused because letting this route
    /// reach the caller's own account would let a stolen administrator session strip that account's
    /// own second factor without ever presenting the password the self-service
    /// <c>/two-factor/disable</c> route demands — see <c>DisableAccountTwoFactorUseCase</c>.
    /// </summary>
    [Fact]
    public async Task AnAdministrator_CannotDisableTwoFactorOnTheirOwnAccount()
    {
        var (adminClient, _, admin) = await SignInAsAdministratorAsync();

        using var response = await adminClient.DeleteAsync(
            new Uri($"{_accountsRoute}/{admin.UserId}/two-factor", UriKind.Relative), TestToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task DisablingTwoFactorOnAnUnknownAccount_Is404()
    {
        var (adminClient, _, _) = await SignInAsAdministratorAsync();

        using var response = await adminClient.DeleteAsync(
            new Uri($"{_accountsRoute}/{Guid.NewGuid()}/two-factor", UriKind.Relative), TestToken);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    /// <summary>
    /// The gap this endpoint exists to close: a caller who has lost the authenticator app <em>and</em>
    /// the recovery codes can prove neither a code nor — once the second factor is armed — a
    /// completed login at all, and had no recourse before this route existed short of deleting the
    /// account outright. No password, no code: an administrator's own session is what this
    /// capability is gated on instead.
    /// </summary>
    [Fact]
    public async Task AnAdministrator_CanDisableTwoFactorOnAnAccountThatLostItsSecondFactor()
    {
        var (adminClient, _, _) = await SignInAsAdministratorAsync();
        var (targetClient, targetUser, targetSession) = await SignInAsync();
        await TwoFactorTestSupport.EnableTwoFactorAsync(targetClient, targetUser, targetSession, TestToken);

        using var disabled = await adminClient.DeleteAsync(
            new Uri($"{_accountsRoute}/{targetSession.UserId}/two-factor", UriKind.Relative), TestToken);
        disabled.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        // The escape hatch actually worked: the account signs in in one step again, with no
        // authenticator app and no recovery code in sight.
        using var loginAfterDisable = await CreateClient().PostAsJsonAsync(
            $"{AuthRoute}/login", new LoginRequest(targetUser.Email, targetUser.Password), TestToken);
        loginAfterDisable.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await ApiJson.ReadAsync<LoginResponse>(loginAfterDisable, TestToken))
            .ShouldBeOfType<LoginResponse.Authenticated>();
    }

    /// <summary>
    /// The gap this endpoint exists to close, mirroring
    /// <see cref="LockingAnAccount_InvalidatesTheAccessTokenAlreadyInCirculation"/>: without the
    /// rotation inside <c>TwoFactorAdministration.DisableAsync</c>, a token issued while the second
    /// factor was still armed would keep validating for as long as it has left to live.
    /// </summary>
    [Fact]
    public async Task DisablingTwoFactorAsAdministrator_InvalidatesTheAccessTokenAlreadyInCirculation()
    {
        var (adminClient, _, _) = await SignInAsAdministratorAsync();
        var (targetClient, targetUser, targetSession) = await SignInAsync();
        var (sharedKey, _) = await TwoFactorTestSupport.EnableTwoFactorAsync(targetClient, targetUser, targetSession, TestToken);

        // Confirming revoked every refresh token; sign back in the long way to hold a token this
        // admin action then has to invalidate.
        var loginClient = CreateClient();
        var challenge = await TwoFactorTestSupport.LoginExpectingChallengeAsync(loginClient, targetUser, TestToken);

        using var verifyResponse = await loginClient.PostAsJsonAsync(
            $"{AuthRoute}/login/two-factor",
            new VerifyTwoFactorRequest(challenge.ChallengeToken, AuthenticatorCodes.CurrentCodeFor(sharedKey)),
            TestToken);
        var tokens = (await ApiJson.ReadAsync<LoginResponse>(verifyResponse, TestToken))
            .ShouldBeOfType<LoginResponse.Authenticated>().Tokens;
        loginClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);

        using var before = await loginClient.GetAsync(new Uri($"{AuthRoute}/me", UriKind.Relative), TestToken);
        before.StatusCode.ShouldBe(HttpStatusCode.OK);

        using var disabled = await adminClient.DeleteAsync(
            new Uri($"{_accountsRoute}/{targetSession.UserId}/two-factor", UriKind.Relative), TestToken);
        disabled.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        // The exact same token, already issued, replayed unchanged.
        using var after = await loginClient.GetAsync(new Uri($"{AuthRoute}/me", UriKind.Relative), TestToken);
        after.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        after.Headers.GetValues(ApiFactory.AuthFailureHeader)
            .ShouldContain("This token's security stamp is no longer valid.");
    }

    /// <summary>A confirmed account, signed in, then promoted to <c>Administrator</c> and signed in again
    /// so the token it ends up holding actually carries the role claim the policy checks.</summary>
    private async Task<(HttpClient Client, TestUser User, TestSession Session)> SignInAsAdministratorAsync()
    {
        var (client, user, session) = await SignInAsync();

        await GrantRoleAsync(session.UserId, IdentityRoles.Administrator);

        var tokens = await LoginAsync(client, user);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);

        return (client, user, session with { Tokens = tokens });
    }

    private async Task GrantRoleAsync(Guid userId, string role)
    {
        await EnsureRoleExistsAsync(role);

        await using var scope = Fixture.Factory.Services.CreateAsyncScope();

        var roles = scope.ServiceProvider.GetRequiredService<IRoleAssignments>();
        var change = await roles.AddRoleAsync(userId, role, TestToken);

        if (change.Outcome != RoleAssignmentChangeOutcome.Applied)
        {
            throw new InvalidOperationException(
                $"Granting role '{role}' to '{userId}' failed with {change.Outcome}: {change.RejectionMessage}");
        }
    }

    /// <summary>
    /// Puts the role's own catalog row in place. <see cref="IRoleAssignments"/> only assigns a role
    /// that already exists — it never invents one, the same as <c>AddToRoleAsync</c> underneath it —
    /// and the row <c>IdentitySeeder</c> would normally create is absent here because
    /// <c>IdentitySeed:Enabled</c> is <c>false</c> for the whole suite. Raw SQL against the schema
    /// rather than <c>AppRole</c>, which by this project's own convention (see
    /// <c>AdministratorPolicyTests</c>) an integration test may not name.
    /// </summary>
    private async Task EnsureRoleExistsAsync(string role)
    {
        string normalizedRole = role.ToUpperInvariant();

        await Database.ExecuteAsync(
            $"""
            INSERT INTO {AppDbContext.IdentitySchema}."Role" ("Id", "Name", "NormalizedName", "ConcurrencyStamp")
            VALUES ('{Guid.CreateVersion7()}', '{role}', '{normalizedRole}', '{Guid.CreateVersion7()}')
            ON CONFLICT ("NormalizedName") DO NOTHING
            """,
            TestToken);
    }
}

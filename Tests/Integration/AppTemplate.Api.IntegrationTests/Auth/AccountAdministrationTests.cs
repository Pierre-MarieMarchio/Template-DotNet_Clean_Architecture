using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using AppTemplate.Api.Common.Contracts;
using AppTemplate.Api.Features.Auth.Contracts.Requests;
using AppTemplate.Api.Features.Auth.Contracts.Responses;
using AppTemplate.Api.IntegrationTests.Infrastructure;
using AppTemplate.Application.Auth.Features.Accounts.Model.Policies;
using AppTemplate.Application.Auth.Features.Accounts.Ports.AccountAdministration;
using AppTemplate.Infrastructure.Auth.Common.Contexts;
using AppTemplate.Infrastructure.Auth.Features.Accounts.Configuration.Seeding;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace AppTemplate.Api.IntegrationTests.Auth;

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

    [Fact]
    public async Task RevokingARole_InvalidatesTheAccessTokenAlreadyInCirculation()
    {
        var (adminClient, _, _) = await SignInAsAdministratorAsync();
        var (targetClient, targetUser, target) = await SignInAsync();

        await GrantRoleAsync(target.UserId, IdentityRoles.Administrator);

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

    [Fact]
    public async Task AnAdministrator_CannotDeleteTheirOwnAccount()
    {
        var (adminClient, _, admin) = await SignInAsAdministratorAsync();

        using var response = await adminClient.DeleteAsync(
            new Uri($"{_accountsRoute}/{admin.UserId}", UriKind.Relative), TestToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

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

    [Fact]
    public async Task AnAdministrator_CanDisableTwoFactorOnAnAccountThatLostItsSecondFactor()
    {
        var (adminClient, _, _) = await SignInAsAdministratorAsync();
        var (targetClient, targetUser, targetSession) = await SignInAsync();
        await TwoFactorTestSupport.EnableTwoFactorAsync(targetClient, targetUser, targetSession, TestToken);

        using var disabled = await adminClient.DeleteAsync(
            new Uri($"{_accountsRoute}/{targetSession.UserId}/two-factor", UriKind.Relative), TestToken);
        disabled.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        using var loginAfterDisable = await CreateClient().PostAsJsonAsync(
            $"{AuthRoute}/login", new LoginRequest(targetUser.Email, targetUser.Password), TestToken);
        loginAfterDisable.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await ApiJson.ReadAsync<LoginResponse>(loginAfterDisable, TestToken))
            .ShouldBeOfType<LoginResponse.Authenticated>();
    }

    [Fact]
    public async Task DisablingTwoFactorAsAdministrator_InvalidatesTheAccessTokenAlreadyInCirculation()
    {
        var (adminClient, _, _) = await SignInAsAdministratorAsync();
        var (targetClient, targetUser, targetSession) = await SignInAsync();
        var (sharedKey, _) = await TwoFactorTestSupport.EnableTwoFactorAsync(targetClient, targetUser, targetSession, TestToken);

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

        using var after = await loginClient.GetAsync(new Uri($"{AuthRoute}/me", UriKind.Relative), TestToken);
        after.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        after.Headers.GetValues(ApiFactory.AuthFailureHeader)
            .ShouldContain("This token's security stamp is no longer valid.");
    }

    [Fact]
    public async Task AnAdministrator_ListsTheAccounts_WhoseAddressStartsWithAPrefix_IgnoringCase()
    {
        var (adminClient, _, _) = await SignInAsAdministratorAsync();
        string prefix = $"listed{Guid.NewGuid():N}";
        var first = await RegisterConfirmedUserAsync(CreateClient(), $"{prefix}-a");
        var second = await RegisterConfirmedUserAsync(CreateClient(), $"{prefix}-b");
        await RegisterConfirmedUserAsync(CreateClient(), $"other{Guid.NewGuid():N}");

        var page = await ListAsync(adminClient, $"?email={prefix.ToUpperInvariant()}");

        page.Items.Select(account => account.Email).ShouldBe([first.Email, second.Email]);
        page.TotalCount.ShouldBe(2);
        page.Items.ShouldAllBe(account => account.EmailConfirmed && !account.LockedOut && account.Roles.Count == 0);
    }

    [Fact]
    public async Task AListedAccount_ShowsItsLockoutAndItsRoles()
    {
        var (adminClient, _, _) = await SignInAsAdministratorAsync();
        string prefix = $"state{Guid.NewGuid():N}";
        var (_, targetUser, target) = await SignInAsync(prefix);

        using var locked = await adminClient.PostAsync(
            new Uri($"{_accountsRoute}/{target.UserId}/lockout", UriKind.Relative), content: null, TestToken);
        locked.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        using var granted = await adminClient.PutAsync(
            new Uri($"{_accountsRoute}/{target.UserId}/roles/{IdentityRoles.Administrator}", UriKind.Relative),
            content: null,
            TestToken);
        granted.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var account = (await ListAsync(adminClient, $"?email={prefix}")).Items.ShouldHaveSingleItem();

        account.Id.ShouldBe(target.UserId);
        account.Email.ShouldBe(targetUser.Email);
        account.LockedOut.ShouldBeTrue();
        account.Roles.ShouldBe([IdentityRoles.Administrator]);
    }

    [Fact]
    public async Task TheAccountList_IsPaged()
    {
        var (adminClient, _, _) = await SignInAsAdministratorAsync();
        string prefix = $"paged{Guid.NewGuid():N}";
        await RegisterConfirmedUserAsync(CreateClient(), $"{prefix}-a");
        await RegisterConfirmedUserAsync(CreateClient(), $"{prefix}-b");

        var firstPage = await ListAsync(adminClient, $"?email={prefix}&page=1&pageSize=1");
        var secondPage = await ListAsync(adminClient, $"?email={prefix}&page=2&pageSize=1");

        firstPage.HasNextPage.ShouldBeTrue();
        firstPage.TotalPages.ShouldBe(2);
        secondPage.HasNextPage.ShouldBeFalse();
        secondPage.Items.ShouldHaveSingleItem().Email.ShouldNotBe(firstPage.Items.ShouldHaveSingleItem().Email);
    }

    [Fact]
    public async Task APageSizeAboveTheCeiling_IsRefused()
    {
        var (adminClient, _, _) = await SignInAsAdministratorAsync();

        using var response = await adminClient.GetAsync(
            new Uri($"{_accountsRoute}?pageSize={AccountCollectionPolicy.Instance.MaxPageSize + 1}", UriKind.Relative),
            TestToken);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ApiJson.ReadProblemAsync(response, TestToken)).Code.ShouldBe("paging.invalid");
    }

    [Fact]
    public async Task AnOrdinaryAuthenticatedUser_CannotListTheAccounts()
    {
        var (client, _, _) = await SignInAsync();

        using var response = await client.GetAsync(new Uri(_accountsRoute, UriKind.Relative), TestToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task AnUnauthenticatedCaller_CannotListTheAccounts()
    {
        using var response = await CreateClient().GetAsync(new Uri(_accountsRoute, UriKind.Relative), TestToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    private static async Task<PagedResponse<AccountSummaryResponse>> ListAsync(HttpClient client, string query)
    {
        using var response = await client.GetAsync(new Uri($"{_accountsRoute}{query}", UriKind.Relative), TestToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        return await ApiJson.ReadAsync<PagedResponse<AccountSummaryResponse>>(response, TestToken);
    }

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

        var administration = scope.ServiceProvider.GetRequiredService<IAccountAdministrationService>();
        var change = await administration.GrantRoleAsync(userId, role, TestToken);

        if (change.Status != AccountAdministrationStatus.Applied)
        {
            throw new InvalidOperationException(
                $"Granting role '{role}' to '{userId}' failed with {change.Status}: {change.RejectionMessage}");
        }
    }

    private async Task EnsureRoleExistsAsync(string role)
    {
        string normalizedRole = role.ToUpperInvariant();

        await Database.ExecuteAsync(
            $"""
            INSERT INTO {AuthDbContext.IdentitySchema}."Role" ("Id", "Name", "NormalizedName", "ConcurrencyStamp")
            VALUES ('{Guid.CreateVersion7()}', '{role}', '{normalizedRole}', '{Guid.CreateVersion7()}')
            ON CONFLICT ("NormalizedName") DO NOTHING
            """,
            TestToken);
    }
}

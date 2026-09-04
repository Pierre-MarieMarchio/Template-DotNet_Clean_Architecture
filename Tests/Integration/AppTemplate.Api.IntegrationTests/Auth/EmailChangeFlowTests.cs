using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using AppTemplate.Api.Features.Auth.Contracts.Requests;
using AppTemplate.Api.Features.Auth.Contracts.Responses;
using AppTemplate.Api.IntegrationTests.Infrastructure;
using Shouldly;
using Xunit;

namespace AppTemplate.Api.IntegrationTests.Auth;

/// <summary>
/// End to end coverage of <c>change-email</c> and <c>confirm-email-change</c>: two tokens, two
/// addresses, a rotation the framework performs and a revocation this codebase performs by hand,
/// and an anti-enumeration path on the new address.
/// </summary>
public sealed class EmailChangeFlowTests(ApiFixture fixture) : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task ChangeEmail_WithTheCorrectPassword_SwapsWhichAddressSignsIn()
    {
        var (client, user, _) = await SignInAsync();
        string newEmail = NewAddress();

        // SignInAsync registers first, which already sent the confirmation mail this test is not
        // about: cleared so what follows can check what was and was not sent from a known-empty
        // mailbox.
        Emails.Clear();

        using var requested = await client.PostAsJsonAsync(
            $"{AuthRoute}/change-email",
            new RequestEmailChangeRequest(user.Password, newEmail),
            TestToken);
        requested.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        // Mailed to the new address, never the old one.
        var sent = RequireLastEmailTo(newEmail);
        sent.Subject.ShouldBe("Confirm your new email address");
        Emails.LastTo(user.Email).ShouldBeNull();

        var (confirmedEmail, token) = ReadEmailLink(sent);
        confirmedEmail.ShouldBe(newEmail);

        using var confirmed = await client.PostAsJsonAsync(
            $"{AuthRoute}/confirm-email-change",
            new ConfirmEmailChangeRequest(confirmedEmail, token),
            TestToken);
        confirmed.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        using var loginWithOld = await client.PostAsJsonAsync(
            $"{AuthRoute}/login", new LoginRequest(user.Email, user.Password), TestToken);
        loginWithOld.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        // ChangeEmailAsync marks the new address confirmed as part of applying the change, so signing
        // in with it works immediately — no separate confirm-email round trip is needed.
        using var loginWithNew = await client.PostAsJsonAsync(
            $"{AuthRoute}/login", new LoginRequest(newEmail, user.Password), TestToken);
        loginWithNew.StatusCode.ShouldBe(HttpStatusCode.OK);

        var authenticated = (await ApiJson.ReadAsync<LoginResponse>(loginWithNew, TestToken))
            .ShouldBeOfType<LoginResponse.Authenticated>();

        using var profileRequest = new HttpRequestMessage(HttpMethod.Get, $"{AuthRoute}/me");
        profileRequest.Headers.Authorization =
            new AuthenticationHeaderValue("Bearer", authenticated.Tokens.AccessToken);
        using var profileResponse = await client.SendAsync(profileRequest, TestToken);

        var profile = await ApiJson.ReadAsync<CurrentUserResponse>(profileResponse, TestToken);
        profile.Email.ShouldBe(newEmail);

        // The decision this vertical had to make explicit: Register lets a caller pick a UserName
        // independent of Email, so changing the address does not silently rename the account too.
        profile.UserName.ShouldBe(user.UserName);
    }

    [Fact]
    public async Task ChangeEmail_WithTheWrongCurrentPassword_IsRejectedAndLeavesItUnchanged()
    {
        var (client, user, _) = await SignInAsync();
        Emails.Clear();

        using var rejected = await client.PostAsJsonAsync(
            $"{AuthRoute}/change-email",
            new RequestEmailChangeRequest("Definitely-Not-It1", NewAddress()),
            TestToken);

        rejected.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var problem = await ApiJson.ReadProblemAsync(rejected, TestToken);
        problem.Code.ShouldBe("request.validationFailed");
        problem.Body.ShouldContain("currentPassword", Case.Sensitive);

        Emails.Snapshot().ShouldBeEmpty();

        using var stillWorks = await client.PostAsJsonAsync(
            $"{AuthRoute}/login", new LoginRequest(user.Email, user.Password), TestToken);
        stillWorks.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    /// <summary>
    /// The anti-enumeration property and the no-plaintext-logging one, together, on the address being
    /// moved <em>to</em> rather than the caller's own: a request naming an address already registered
    /// to someone else must answer exactly as a request naming a free one does.
    /// </summary>
    [Fact]
    public async Task RequestEmailChange_ToAnAlreadyRegisteredAddress_AnswersIdenticallyAndSendsNothing()
    {
        var registrationClient = CreateClient();
        var target = await RegisterConfirmedUserAsync(registrationClient, "already-taken");

        var (client, user, _) = await SignInAsync("change-requester");
        string freeAddress = NewAddress();

        using var toFreeAddress = await client.PostAsJsonAsync(
            $"{AuthRoute}/change-email",
            new RequestEmailChangeRequest(user.Password, freeAddress),
            TestToken);
        toFreeAddress.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        // Proof the free-address branch really sent something, so the silence checked below means
        // "suppressed" rather than "nothing was ever going to be checked".
        RequireLastEmailTo(freeAddress).Subject.ShouldBe("Confirm your new email address");
        Emails.Clear();

        using var toTakenAddress = await client.PostAsJsonAsync(
            $"{AuthRoute}/change-email",
            new RequestEmailChangeRequest(user.Password, target.Email),
            TestToken);

        toTakenAddress.StatusCode.ShouldBe(toFreeAddress.StatusCode);

        Emails.Snapshot().ShouldBeEmpty("A request naming an address already taken must send nothing.");

        Fixture.Logs.Snapshot()
            .Where(record => record.Message.Contains(target.Email, StringComparison.Ordinal))
            .ShouldBeEmpty("The address must never reach a log on this anti-enumeration path.");
    }

    /// <summary>
    /// Mirrors the guarantee <c>ChangePassword</c> gives: the stamp rotation that
    /// <c>ChangeEmailAsync</c> performs fails the access token already in circulation before it would
    /// otherwise expire.
    /// </summary>
    [Fact]
    public async Task ConfirmEmailChange_InvalidatesTheAccessTokenAlreadyInCirculation()
    {
        var (client, user, _) = await SignInAsync();

        using var before = await client.GetAsync(new Uri($"{AuthRoute}/me", UriKind.Relative), TestToken);
        before.StatusCode.ShouldBe(HttpStatusCode.OK);

        var (confirmedEmail, token) = await RequestAndReadConfirmationAsync(client, user.Password);

        using var confirmed = await client.PostAsJsonAsync(
            $"{AuthRoute}/confirm-email-change",
            new ConfirmEmailChangeRequest(confirmedEmail, token),
            TestToken);
        confirmed.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        // The exact same token, already issued, replayed unchanged.
        using var after = await client.GetAsync(new Uri($"{AuthRoute}/me", UriKind.Relative), TestToken);
        after.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        after.Headers.GetValues(ApiFactory.AuthFailureHeader)
            .ShouldContain("This token's security stamp is no longer valid.");
    }

    /// <summary>
    /// The stamp rotation kills access tokens on its own; refresh tokens are this codebase's own and
    /// survive it untouched unless <c>ConfirmEmailChangeUseCase</c> revokes them itself through
    /// <c>CredentialInvalidationPolicy</c>.
    /// </summary>
    [Fact]
    public async Task ConfirmEmailChange_RevokesEveryRefreshTokenForTheAccount()
    {
        var (client, user, session) = await SignInAsync();

        var (confirmedEmail, token) = await RequestAndReadConfirmationAsync(client, user.Password);

        using var confirmed = await client.PostAsJsonAsync(
            $"{AuthRoute}/confirm-email-change",
            new ConfirmEmailChangeRequest(confirmedEmail, token),
            TestToken);
        confirmed.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        using var refreshed = await client.PostAsJsonAsync(
            $"{AuthRoute}/refresh",
            new RefreshAccessTokenRequest(session.Tokens.RefreshToken),
            TestToken);
        refreshed.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await ApiJson.ReadProblemAsync(refreshed, TestToken)).Code.ShouldBe("auth.refreshToken.invalid");
    }

    [Fact]
    public async Task ChangeEmail_WithoutAnAccessToken_IsRefused()
    {
        var client = CreateClient();

        using var response = await client.PostAsJsonAsync(
            $"{AuthRoute}/change-email",
            new RequestEmailChangeRequest("whatever", NewAddress()),
            TestToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ConfirmEmailChange_WithoutAnAccessToken_IsRefused()
    {
        var client = CreateClient();

        using var response = await client.PostAsJsonAsync(
            $"{AuthRoute}/confirm-email-change",
            new ConfirmEmailChangeRequest(NewAddress(), "some-token"),
            TestToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    private async Task<(string Email, string Token)> RequestAndReadConfirmationAsync(HttpClient client, string password)
    {
        string newEmail = NewAddress();

        using var requested = await client.PostAsJsonAsync(
            $"{AuthRoute}/change-email",
            new RequestEmailChangeRequest(password, newEmail),
            TestToken);

        if (requested.StatusCode != HttpStatusCode.NoContent)
        {
            throw new InvalidOperationException(
                $"Requesting a change to {newEmail} failed with {(int)requested.StatusCode}: " +
                await requested.Content.ReadAsStringAsync(TestToken));
        }

        return ReadEmailLink(RequireLastEmailTo(newEmail));
    }

    private static string NewAddress() => $"changed-{Guid.CreateVersion7():N}@integration.test";
}

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using AppTemplate.Api.Features.Auth.Contracts.Requests;
using AppTemplate.Api.Features.Auth.Contracts.Responses;
using AppTemplate.Api.IntegrationTests.Infrastructure;
using AppTemplate.Infrastructure.Auth.Common.Contexts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace AppTemplate.Api.IntegrationTests.Auth;

public sealed class EmailChangeFlowTests(ApiFixture fixture) : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task ChangeEmail_WithTheCorrectPassword_SwapsWhichAddressSignsIn()
    {
        var (client, user, _) = await SignInAsync();
        string newEmail = NewAddress();

        Emails.Clear();

        using var requested = await client.PostAsJsonAsync(
            $"{AuthRoute}/change-email",
            new RequestEmailChangeRequest(user.Password, newEmail),
            TestToken);
        requested.StatusCode.ShouldBe(HttpStatusCode.NoContent);

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

        await using var scope = Fixture.Factory.Services.CreateAsyncScope();
        var stored = await scope.ServiceProvider.GetRequiredService<AuthDbContext>().Users
            .AsNoTracking()
            .SingleAsync(account => account.Email == newEmail, TestToken);

        stored.UserName.ShouldBe(newEmail);
        stored.NormalizedUserName.ShouldBe(newEmail.ToUpperInvariant());
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

    [Fact]
    public async Task RequestEmailChange_ToAnAlreadyRegisteredAddress_AnswersIdentically_AndMailsTheNoticeThere()
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

        RequireLastEmailTo(freeAddress).Subject.ShouldBe("Confirm your new email address");
        Emails.Clear();

        using var toTakenAddress = await client.PostAsJsonAsync(
            $"{AuthRoute}/change-email",
            new RequestEmailChangeRequest(user.Password, target.Email),
            TestToken);

        toTakenAddress.StatusCode.ShouldBe(toFreeAddress.StatusCode);
        (await toTakenAddress.Content.ReadAsStringAsync(TestToken))
            .ShouldBe(await toFreeAddress.Content.ReadAsStringAsync(TestToken));

        Emails.Snapshot().ShouldHaveSingleItem().Recipient.ShouldBe(target.Email);
        RequireLastEmailTo(target.Email).Subject.ShouldBe("You already have an account");

        Fixture.Logs.Snapshot()
            .Where(record => record.Message.Contains(target.Email, StringComparison.Ordinal))
            .ShouldBeEmpty("The address must never reach a log on this anti-enumeration path.");
    }

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

        using var after = await client.GetAsync(new Uri($"{AuthRoute}/me", UriKind.Relative), TestToken);
        after.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        after.Headers.GetValues(ApiFactory.AuthFailureHeader)
            .ShouldContain("This token's security stamp is no longer valid.");
    }

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

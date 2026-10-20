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

/// <summary>
/// Register, confirm, log in — end to end, with the confirmation token taken out of the email that
/// was actually sent.
/// </summary>
public sealed class RegistrationFlowTests(ApiFixture fixture) : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task TheWholeFlow_RegisterThenConfirmThenLogIn()
    {
        var client = CreateClient();
        string email = $"newcomer-{Guid.CreateVersion7():N}@integration.test";

        using var registered = await client.PostAsJsonAsync(
            $"{AuthRoute}/register",
            new RegisterRequest(email, ValidPassword),
            TestToken);

        registered.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        (await ApiJson.ReadAsync<RegisterResponse>(registered, TestToken)).ShouldBe(RegisterResponse.Pending);

        // Exactly one email, to that address, with the configured subject.
        var sent = RequireLastEmailTo(email);
        Emails.Snapshot().Count.ShouldBe(1);
        sent.Subject.ShouldBe("Confirm your email address");
        sent.SentAt.ShouldBe(Clock.UtcNow);

        // Signing in before confirming must fail, and fail the same way every other login failure
        // does.
        using var tooEarly = await client.PostAsJsonAsync(
            $"{AuthRoute}/login",
            new LoginRequest(email, ValidPassword),
            TestToken);
        tooEarly.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await ApiJson.ReadProblemAsync(tooEarly, TestToken)).Code.ShouldBe("auth.login.invalidCredentials");

        var (confirmedEmail, token) = ReadEmailLink(sent);
        confirmedEmail.ShouldBe(email);
        token.ShouldNotBeNullOrWhiteSpace();

        using var confirmed = await client.PostAsJsonAsync(
            $"{AuthRoute}/confirm-email",
            new ConfirmEmailRequest(confirmedEmail, token),
            TestToken);
        confirmed.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await EmailConfirmedAsync(email)).ShouldBeTrue();

        using var loggedIn = await client.PostAsJsonAsync(
            $"{AuthRoute}/login",
            new LoginRequest(email, ValidPassword),
            TestToken);
        loggedIn.StatusCode.ShouldBe(HttpStatusCode.OK);

        // Signing in answers with tokens and nothing else: a caller that wants the account it just
        // signed in as asks the profile endpoint.
        var authenticated = (await ApiJson.ReadAsync<LoginResponse>(loggedIn, TestToken))
            .ShouldBeOfType<LoginResponse.Authenticated>();
        authenticated.Tokens.AccessToken.ShouldNotBeNullOrWhiteSpace();

        // The access token is a working credential, which is the only thing that makes the whole
        // flow worth anything.
        using var protectedRequest = new HttpRequestMessage(HttpMethod.Get, ProtectedRoute);
        protectedRequest.Headers.Authorization =
            new AuthenticationHeaderValue("Bearer", authenticated.Tokens.AccessToken);

        using var reached = await client.SendAsync(protectedRequest, TestToken);
        reached.StatusCode.ShouldBe(HttpStatusCode.OK);

        // And the profile it opens describes the account that was just registered.
        using var profileRequest = new HttpRequestMessage(HttpMethod.Get, $"{AuthRoute}/me");
        profileRequest.Headers.Authorization =
            new AuthenticationHeaderValue("Bearer", authenticated.Tokens.AccessToken);

        using var profileResponse = await client.SendAsync(profileRequest, TestToken);
        profileResponse.StatusCode.ShouldBe(HttpStatusCode.OK);

        var profile = await ApiJson.ReadAsync<CurrentUserResponse>(profileResponse, TestToken);
        profile.UserId.ShouldNotBe(Guid.Empty);
        profile.Email.ShouldBe(email);
        profile.EmailConfirmed.ShouldBeTrue();
    }

    /// <summary>
    /// The confirmation parameters travel in the link's <em>fragment</em>, which browsers never send
    /// to a server, so the single-use token stays out of access logs and <c>Referer</c> headers.
    /// </summary>
    [Fact]
    public async Task TheConfirmationLink_CarriesItsSecretInTheFragment()
    {
        var client = CreateClient();
        var user = await RegisterUserAsync(client, "fragment");

        var sent = RequireLastEmailTo(user.Email);
        var (_, token) = ReadEmailLink(sent);

        string href = sent.HtmlBody[(sent.HtmlBody.IndexOf("href=\"", StringComparison.Ordinal) + 6)..];
        href = href[..href.IndexOf('"', StringComparison.Ordinal)];
        string decoded = WebUtility.HtmlDecode(href);
        var uri = new Uri(decoded, UriKind.Absolute);

        uri.Query.ShouldBeEmpty();
        uri.Fragment.ShouldNotBeEmpty();
        uri.GetLeftPart(UriPartial.Path).ShouldBe("https://client.integration.test/confirm-email");

        // And the token is not in the part of the URL a server would see.
        uri.GetLeftPart(UriPartial.Query).ShouldNotContain(token);
    }

    [Fact]
    public async Task ATamperedConfirmationToken_IsRejected()
    {
        var client = CreateClient();
        var user = await RegisterUserAsync(client, "tampered");
        var (email, token) = ReadEmailLink(RequireLastEmailTo(user.Email));

        using var response = await client.PostAsJsonAsync(
            $"{AuthRoute}/confirm-email",
            new ConfirmEmailRequest(email, token + "x"),
            TestToken);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ApiJson.ReadProblemAsync(response, TestToken)).Code.ShouldBe("auth.confirmEmail.invalid");

        (await EmailConfirmedAsync(user.Email)).ShouldBeFalse();
    }

    /// <summary>
    /// Confirming for an address nobody registered must answer exactly as a wrong token does, or the
    /// endpoint tells an attacker which addresses exist.
    /// </summary>
    [Fact]
    public async Task AnUnknownAddress_AndAWrongToken_AreIndistinguishable()
    {
        var client = CreateClient();
        var user = await RegisterUserAsync(client, "known");
        var (email, token) = ReadEmailLink(RequireLastEmailTo(user.Email));

        using var wrongToken = await client.PostAsJsonAsync(
            $"{AuthRoute}/confirm-email",
            new ConfirmEmailRequest(email, token + "x"),
            TestToken);

        using var unknownAddress = await client.PostAsJsonAsync(
            $"{AuthRoute}/confirm-email",
            new ConfirmEmailRequest("nobody-at-all@integration.test", token),
            TestToken);

        wrongToken.StatusCode.ShouldBe(unknownAddress.StatusCode);

        var first = await ApiJson.ReadProblemAsync(wrongToken, TestToken);
        var second = await ApiJson.ReadProblemAsync(unknownAddress, TestToken);

        first.BodyWithoutTraceId.ShouldBe(second.BodyWithoutTraceId);
    }

    /// <summary>
    /// Registration commits the account before it tries to deliver anything, so a resend has to be
    /// able to produce a fresh, working link.
    /// </summary>
    [Fact]
    public async Task AResentLink_Confirms()
    {
        var client = CreateClient();
        var user = await RegisterUserAsync(client, "resend");

        Emails.Clear();

        using var resent = await client.PostAsJsonAsync(
            $"{AuthRoute}/resend-confirmation-email",
            new ResendConfirmationEmailRequest(user.Email),
            TestToken);
        resent.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var (email, token) = ReadEmailLink(RequireLastEmailTo(user.Email));

        using var confirmed = await client.PostAsJsonAsync(
            $"{AuthRoute}/confirm-email",
            new ConfirmEmailRequest(email, token),
            TestToken);

        confirmed.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await EmailConfirmedAsync(user.Email)).ShouldBeTrue();
    }

    [Fact]
    public async Task EveryStateOfTheAddress_GetsAByteIdenticalAnswer_AndItsOwnMail()
    {
        var client = CreateClient();
        string email = $"probed-{Guid.CreateVersion7():N}@integration.test";

        var unknown = await RegisterAsync(client, email);
        var firstMail = RequireLastEmailTo(email);
        Emails.Clear();

        var takenUnconfirmed = await RegisterAsync(client, email);
        var secondMail = RequireLastEmailTo(email);
        Emails.Clear();

        var (confirmedEmail, token) = ReadEmailLink(secondMail);
        using var confirmed = await client.PostAsJsonAsync(
            $"{AuthRoute}/confirm-email",
            new ConfirmEmailRequest(confirmedEmail, token),
            TestToken);
        confirmed.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        Emails.Clear();

        var takenConfirmed = await RegisterAsync(client, email);
        var thirdMail = RequireLastEmailTo(email);

        takenUnconfirmed.ShouldBe(unknown);
        takenConfirmed.ShouldBe(unknown);
        unknown.Status.ShouldBe(HttpStatusCode.Accepted);
        unknown.Body.ShouldBe("""{"status":"pending"}""");

        firstMail.Subject.ShouldBe("Confirm your email address");
        secondMail.Subject.ShouldBe("Confirm your email address");
        thirdMail.Subject.ShouldBe("You already have an account");
        thirdMail.HtmlBody.ShouldNotContain("href", Case.Insensitive);
        Emails.Snapshot().ShouldHaveSingleItem();

        (await Database.CountAsync("""SELECT count(*) FROM identity."User" """, TestToken)).ShouldBe(1);
    }

    [Fact]
    public async Task SimultaneousRegistrations_OfOneAddress_CreateOneAccount_AndAllAnswerTheSame()
    {
        var client = CreateClient();
        string email = $"racing-{Guid.CreateVersion7():N}@integration.test";

        var answers = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => RegisterAsync(client, email)));

        answers.ShouldAllBe(answer => answer == answers[0]);
        answers[0].Status.ShouldBe(HttpStatusCode.Accepted);
        (await Database.CountAsync("""SELECT count(*) FROM identity."User" """, TestToken)).ShouldBe(1);
        Emails.Snapshot().ShouldAllBe(sent => sent.Recipient == email);
    }

    private static async Task<RegistrationAnswer> RegisterAsync(HttpClient client, string email)
    {
        using var response = await client.PostAsJsonAsync(
            $"{AuthRoute}/register",
            new RegisterRequest(email, ValidPassword),
            TestToken);

        return new RegistrationAnswer(
            response.StatusCode,
            response.Content.Headers.ContentType?.ToString(),
            response.Headers.Location,
            await response.Content.ReadAsStringAsync(TestToken));
    }

    private sealed record RegistrationAnswer(HttpStatusCode Status, string? ContentType, Uri? Location, string Body);

    private async Task<bool> EmailConfirmedAsync(string email)
    {
        await using var scope = Fixture.Factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<AuthDbContext>();

        return await context.Users
            .AsNoTracking()
            .Where(user => user.Email == email)
            .Select(user => user.EmailConfirmed)
            .SingleAsync(TestToken);
    }
}

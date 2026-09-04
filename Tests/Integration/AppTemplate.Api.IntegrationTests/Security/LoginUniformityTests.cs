using System.Net;
using System.Net.Http.Json;
using AppTemplate.Api.IntegrationTests.Infrastructure;
using AppTemplate.Application.Features.Auth.Dtos;
using AppTemplate.Application.Features.Auth.UseCases.Commands;
using Shouldly;
using Xunit;

namespace AppTemplate.Api.IntegrationTests.Security;

/// <summary>
/// Every way of failing to log in looks the same from outside. A different answer for "no such
/// email" than for "wrong password" turns the endpoint into a user directory.
/// </summary>
public sealed class LoginUniformityTests(ApiFixture fixture) : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task AnUnknownEmailAndAWrongPassword_AreIndistinguishable()
    {
        var setup = CreateClient();
        var user = await RegisterConfirmedUserAsync(setup);

        // Both failures on one client, so nothing about the transport differs either.
        var attempts = CreateClient();

        var unknownEmail = await AttemptAsync(attempts, "nobody-at-all@integration.test", ValidPassword);
        var wrongPassword = await AttemptAsync(attempts, user.Email, "Wrong!Password9");

        unknownEmail.Status.ShouldBe(401);
        unknownEmail.Code.ShouldBe("auth.login.invalidCredentials");

        wrongPassword.Status.ShouldBe(unknownEmail.Status);
        wrongPassword.Code.ShouldBe(unknownEmail.Code);
        wrongPassword.Title.ShouldBe(unknownEmail.Title);
        wrongPassword.Detail.ShouldBe(unknownEmail.Detail);

        // The whole body, not only the fields the suite names: a difference anywhere in it is a
        // difference an attacker can measure.
        wrongPassword.Body.ShouldBe(unknownEmail.Body);
    }

    /// <summary>
    /// An account that exists but has not confirmed its address must not be distinguishable either —
    /// otherwise the endpoint answers "this address is registered" for anybody who asks.
    /// </summary>
    [Fact]
    public async Task AnUnconfirmedAccount_IsIndistinguishableFromAnUnknownOne()
    {
        var setup = CreateClient();
        var unconfirmed = await RegisterUserAsync(setup, "unconfirmed");

        var attempts = CreateClient();

        var unknownEmail = await AttemptAsync(attempts, "nobody-at-all@integration.test", ValidPassword);
        var notConfirmed = await AttemptAsync(attempts, unconfirmed.Email, unconfirmed.Password);

        notConfirmed.Status.ShouldBe(401);
        notConfirmed.Body.ShouldBe(unknownEmail.Body);
    }

    [Fact]
    public async Task TheCorrectCredentialsOfAConfirmedAccount_Succeed()
    {
        // The control. Without it, a suite in which every login fails would pass the tests above.
        var client = CreateClient();
        var user = await RegisterConfirmedUserAsync(client);

        using var response = await client.PostAsJsonAsync(
            $"{AuthRoute}/login",
            new LoginCommand(user.Email, user.Password),
            TestToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var session = (await ApiJson.ReadAsync<LoginOutcome>(response, TestToken))
            .ShouldBeOfType<LoginOutcome.Authenticated>();
        session.UserId.ShouldBe(user.Id);
        session.AccessToken.ShouldNotBeNullOrWhiteSpace();
        session.RefreshToken.ShouldNotBeNullOrWhiteSpace();
    }

    /// <summary>
    /// The resend endpoint is the other enumeration surface: it always answers 204, whether the
    /// address is unknown, unconfirmed or already confirmed.
    /// </summary>
    [Fact]
    public async Task ResendingConfirmation_AnswersTheSameForEveryKindOfAddress()
    {
        var client = CreateClient();
        var unconfirmed = await RegisterUserAsync(client, "pending");

        Emails.Clear();

        using var forUnknown = await client.PostAsJsonAsync(
            $"{AuthRoute}/resend-confirmation-email",
            new ResendConfirmationEmailCommand("nobody-at-all@integration.test"),
            TestToken);
        forUnknown.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        Emails.LastTo("nobody-at-all@integration.test").ShouldBeNull();

        using var forUnconfirmed = await client.PostAsJsonAsync(
            $"{AuthRoute}/resend-confirmation-email",
            new ResendConfirmationEmailCommand(unconfirmed.Email),
            TestToken);
        forUnconfirmed.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        Emails.LastTo(unconfirmed.Email).ShouldNotBeNull();

        forUnconfirmed.StatusCode.ShouldBe(forUnknown.StatusCode);
    }

    private static async Task<ProblemResponse> AttemptAsync(HttpClient client, string email, string password)
    {
        using var response = await client.PostAsJsonAsync(
            $"{AuthRoute}/login",
            new LoginCommand(email, password),
            TestToken);

        return await ApiJson.ReadProblemAsync(response, TestToken);
    }
}

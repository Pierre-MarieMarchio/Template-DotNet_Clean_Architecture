using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using AppTemplate.Api.Features.Auth.Contracts.Requests;
using AppTemplate.Api.Features.Auth.Contracts.Responses;
using AppTemplate.Api.IntegrationTests.Infrastructure;
using Shouldly;
using Xunit;

namespace AppTemplate.Api.IntegrationTests.Security;

public sealed class LoginUniformityTests(ApiFixture fixture) : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task AnUnknownEmailAndAWrongPassword_AreIndistinguishable()
    {
        var setup = CreateClient();
        var user = await RegisterConfirmedUserAsync(setup);

        var attempts = CreateClient();

        var unknownEmail = await AttemptAsync(attempts, "nobody-at-all@integration.test", ValidPassword);
        var wrongPassword = await AttemptAsync(attempts, user.Email, "Wrong!Password9");

        unknownEmail.Status.ShouldBe(401);
        unknownEmail.Code.ShouldBe("auth.login.invalidCredentials");

        wrongPassword.Status.ShouldBe(unknownEmail.Status);
        wrongPassword.Code.ShouldBe(unknownEmail.Code);
        wrongPassword.Title.ShouldBe(unknownEmail.Title);
        wrongPassword.Detail.ShouldBe(unknownEmail.Detail);

        wrongPassword.BodyWithoutTraceId.ShouldBe(unknownEmail.BodyWithoutTraceId);
    }

    [Fact]
    public async Task AnUnconfirmedAccount_IsIndistinguishableFromAnUnknownOne()
    {
        var setup = CreateClient();
        var unconfirmed = await RegisterUserAsync(setup, "unconfirmed");

        var attempts = CreateClient();

        var unknownEmail = await AttemptAsync(attempts, "nobody-at-all@integration.test", ValidPassword);
        var notConfirmed = await AttemptAsync(attempts, unconfirmed.Email, unconfirmed.Password);

        notConfirmed.Status.ShouldBe(401);
        notConfirmed.BodyWithoutTraceId.ShouldBe(unknownEmail.BodyWithoutTraceId);
    }

    [Fact]
    public async Task TheCorrectCredentialsOfAConfirmedAccount_Succeed()
    {
        var client = CreateClient();
        var user = await RegisterConfirmedUserAsync(client);

        using var response = await client.PostAsJsonAsync(
            $"{AuthRoute}/login",
            new LoginRequest(user.Email, user.Password),
            TestToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var authenticated = (await ApiJson.ReadAsync<LoginResponse>(response, TestToken))
            .ShouldBeOfType<LoginResponse.Authenticated>();
        authenticated.Tokens.AccessToken.ShouldNotBeNullOrWhiteSpace();
        authenticated.Tokens.RefreshToken.ShouldNotBeNullOrWhiteSpace();

        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", authenticated.Tokens.AccessToken);

        using var profile = await client.GetAsync(new Uri($"{AuthRoute}/me", UriKind.Relative), TestToken);
        profile.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await ApiJson.ReadAsync<CurrentUserResponse>(profile, TestToken)).Email.ShouldBe(user.Email);
    }

    [Fact]
    public async Task ResendingConfirmation_AnswersTheSameForEveryKindOfAddress()
    {
        var client = CreateClient();
        var unconfirmed = await RegisterUserAsync(client, "pending");

        Emails.Clear();

        using var forUnknown = await client.PostAsJsonAsync(
            $"{AuthRoute}/resend-confirmation-email",
            new ResendConfirmationEmailRequest("nobody-at-all@integration.test"),
            TestToken);
        forUnknown.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        Emails.LastTo("nobody-at-all@integration.test").ShouldBeNull();

        using var forUnconfirmed = await client.PostAsJsonAsync(
            $"{AuthRoute}/resend-confirmation-email",
            new ResendConfirmationEmailRequest(unconfirmed.Email),
            TestToken);
        forUnconfirmed.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        Emails.LastTo(unconfirmed.Email).ShouldNotBeNull();

        forUnconfirmed.StatusCode.ShouldBe(forUnknown.StatusCode);
    }

    private static async Task<ProblemResponse> AttemptAsync(HttpClient client, string email, string password)
    {
        using var response = await client.PostAsJsonAsync(
            $"{AuthRoute}/login",
            new LoginRequest(email, password),
            TestToken);

        return await ApiJson.ReadProblemAsync(response, TestToken);
    }
}

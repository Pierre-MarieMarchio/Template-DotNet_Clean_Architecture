using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using AppTemplate.Api.Features.Auth.Contracts.Requests;
using AppTemplate.Api.Features.Auth.Contracts.Responses;
using AppTemplate.Api.IntegrationTests.Infrastructure;
using Shouldly;
using Xunit;

namespace AppTemplate.Api.IntegrationTests.Auth;

public sealed class TwoFactorFlowTests(ApiFixture fixture) : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task TheFullTwoStepLoginFlow_WorksEndToEndWithAnAuthenticatorCode()
    {
        var (client, user, session) = await SignInAsync();
        var (sharedKey, _) = await TwoFactorTestSupport.EnableTwoFactorAsync(client, user, session, TestToken);

        var loginClient = CreateClient();
        var challenge = await TwoFactorTestSupport.LoginExpectingChallengeAsync(loginClient, user, TestToken);

        using var verifyResponse = await loginClient.PostAsJsonAsync(
            $"{AuthRoute}/login/two-factor",
            new VerifyTwoFactorRequest(challenge.ChallengeToken, AuthenticatorCodes.CurrentCodeFor(sharedKey)),
            TestToken);

        verifyResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        var authenticated = (await ApiJson.ReadAsync<LoginResponse>(verifyResponse, TestToken))
            .ShouldBeOfType<LoginResponse.Authenticated>();
        authenticated.Tokens.AccessToken.ShouldNotBeNullOrEmpty();
    }

    [Fact]
    public async Task AWrongCode_IsRefusedButTheChallengeSurvivesForARetry()
    {
        var (client, user, session) = await SignInAsync();
        var (sharedKey, _) = await TwoFactorTestSupport.EnableTwoFactorAsync(client, user, session, TestToken);

        var loginClient = CreateClient();
        var challenge = await TwoFactorTestSupport.LoginExpectingChallengeAsync(loginClient, user, TestToken);

        using var wrongResponse = await loginClient.PostAsJsonAsync(
            $"{AuthRoute}/login/two-factor",
            new VerifyTwoFactorRequest(challenge.ChallengeToken, "000000"),
            TestToken);
        wrongResponse.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        using var retryResponse = await loginClient.PostAsJsonAsync(
            $"{AuthRoute}/login/two-factor",
            new VerifyTwoFactorRequest(challenge.ChallengeToken, AuthenticatorCodes.CurrentCodeFor(sharedKey)),
            TestToken);

        retryResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await ApiJson.ReadAsync<LoginResponse>(retryResponse, TestToken)).ShouldBeOfType<LoginResponse.Authenticated>();
    }

    [Fact]
    public async Task ARecoveryCode_CompletesLoginOnceAndIsThenSpent()
    {
        var (client, user, session) = await SignInAsync();
        var (_, recoveryCodes) = await TwoFactorTestSupport.EnableTwoFactorAsync(client, user, session, TestToken);
        string recoveryCode = recoveryCodes[0];

        var firstLoginClient = CreateClient();
        var firstChallenge = await TwoFactorTestSupport.LoginExpectingChallengeAsync(firstLoginClient, user, TestToken);

        using var firstVerifyResponse = await firstLoginClient.PostAsJsonAsync(
            $"{AuthRoute}/login/two-factor",
            new VerifyTwoFactorRequest(firstChallenge.ChallengeToken, recoveryCode),
            TestToken);

        firstVerifyResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await ApiJson.ReadAsync<LoginResponse>(firstVerifyResponse, TestToken))
            .ShouldBeOfType<LoginResponse.Authenticated>();

        var secondLoginClient = CreateClient();
        var secondChallenge = await TwoFactorTestSupport.LoginExpectingChallengeAsync(secondLoginClient, user, TestToken);

        using var replayResponse = await secondLoginClient.PostAsJsonAsync(
            $"{AuthRoute}/login/two-factor",
            new VerifyTwoFactorRequest(secondChallenge.ChallengeToken, recoveryCode),
            TestToken);

        replayResponse.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await ApiJson.ReadProblemAsync(replayResponse, TestToken)).Code
            .ShouldBe("auth.login.invalidTwoFactorChallenge");
    }

    [Fact]
    public async Task DisablingTwoFactor_RestoresAOneStepLogin()
    {
        var (client, user, session) = await SignInAsync();
        var (sharedKey, _) = await TwoFactorTestSupport.EnableTwoFactorAsync(client, user, session, TestToken);

        var loginClient = CreateClient();
        var challenge = await TwoFactorTestSupport.LoginExpectingChallengeAsync(loginClient, user, TestToken);

        using var verifyResponse = await loginClient.PostAsJsonAsync(
            $"{AuthRoute}/login/two-factor",
            new VerifyTwoFactorRequest(challenge.ChallengeToken, AuthenticatorCodes.CurrentCodeFor(sharedKey)),
            TestToken);
        var tokens = (await ApiJson.ReadAsync<LoginResponse>(verifyResponse, TestToken))
            .ShouldBeOfType<LoginResponse.Authenticated>().Tokens;
        loginClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);

        using var disableResponse = await loginClient.PostAsJsonAsync(
            $"{AuthRoute}/two-factor/disable",
            new DisableTwoFactorRequest(user.Password),
            TestToken);
        disableResponse.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var finalLoginClient = CreateClient();

        using var finalLoginResponse = await finalLoginClient.PostAsJsonAsync(
            $"{AuthRoute}/login",
            new LoginRequest(user.Email, user.Password),
            TestToken);

        finalLoginResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await ApiJson.ReadAsync<LoginResponse>(finalLoginResponse, TestToken))
            .ShouldBeOfType<LoginResponse.Authenticated>();
    }

    [Fact]
    public async Task DisablingWithTheWrongPassword_LeavesTwoFactorArmed()
    {
        var (client, user, session) = await SignInAsync();
        var (sharedKey, _) = await TwoFactorTestSupport.EnableTwoFactorAsync(client, user, session, TestToken);

        var loginClient = CreateClient();
        var challenge = await TwoFactorTestSupport.LoginExpectingChallengeAsync(loginClient, user, TestToken);

        using var verifyResponse = await loginClient.PostAsJsonAsync(
            $"{AuthRoute}/login/two-factor",
            new VerifyTwoFactorRequest(challenge.ChallengeToken, AuthenticatorCodes.CurrentCodeFor(sharedKey)),
            TestToken);
        var tokens = (await ApiJson.ReadAsync<LoginResponse>(verifyResponse, TestToken))
            .ShouldBeOfType<LoginResponse.Authenticated>().Tokens;
        loginClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);

        using var disableResponse = await loginClient.PostAsJsonAsync(
            $"{AuthRoute}/two-factor/disable",
            new DisableTwoFactorRequest("the wrong password"),
            TestToken);
        disableResponse.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var laterLoginClient = CreateClient();

        using var laterLoginResponse = await laterLoginClient.PostAsJsonAsync(
            $"{AuthRoute}/login",
            new LoginRequest(user.Email, user.Password),
            TestToken);

        (await ApiJson.ReadAsync<LoginResponse>(laterLoginResponse, TestToken))
            .ShouldBeOfType<LoginResponse.TwoFactorRequired>();
    }

    [Fact]
    public async Task ConfirmingWithTheWrongPassword_ArmsNothing()
    {
        var (client, user, session) = await SignInAsync();
        var setup = await TwoFactorTestSupport.BeginTwoFactorSetupAsync(client, TestToken);
        await TwoFactorTestSupport.RefreshAuthorizationAsync(client, session.Tokens.RefreshToken, TestToken);

        using var wrongPasswordResponse = await client.PostAsJsonAsync(
            $"{AuthRoute}/two-factor/confirm",
            new ConfirmTwoFactorSetupRequest("the wrong password", AuthenticatorCodes.CurrentCodeFor(setup.SharedKey)),
            TestToken);
        wrongPasswordResponse.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        using var loginResponse = await CreateClient().PostAsJsonAsync(
            $"{AuthRoute}/login",
            new LoginRequest(user.Email, user.Password),
            TestToken);
        loginResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await ApiJson.ReadAsync<LoginResponse>(loginResponse, TestToken))
            .ShouldBeOfType<LoginResponse.Authenticated>();

        using var rightPasswordResponse = await client.PostAsJsonAsync(
            $"{AuthRoute}/two-factor/confirm",
            new ConfirmTwoFactorSetupRequest(user.Password, AuthenticatorCodes.CurrentCodeFor(setup.SharedKey)),
            TestToken);
        rightPasswordResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await ApiJson.ReadAsync<ConfirmTwoFactorSetupResponse>(rightPasswordResponse, TestToken))
            .RecoveryCodes.ShouldNotBeEmpty();
    }

    [Fact]
    public async Task ConfirmingTwoFactor_EndsEveryOtherSessionButSparesTheCallersOwn()
    {
        var (client, user, session) = await SignInAsync();
        var setup = await TwoFactorTestSupport.BeginTwoFactorSetupAsync(client, TestToken);

        var otherDevice = CreateClient();
        var otherTokens = await LoginAsync(otherDevice, user);

        using var refreshed = await client.PostAsJsonAsync(
            $"{AuthRoute}/refresh", new RefreshAccessTokenRequest(session.Tokens.RefreshToken), TestToken);
        refreshed.StatusCode.ShouldBe(HttpStatusCode.OK);
        var callersTokens = await ApiJson.ReadAsync<TokenResponse>(refreshed, TestToken);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", callersTokens.AccessToken);

        await TwoFactorTestSupport.ConfirmTwoFactorSetupAsync(
            client, user.Password, AuthenticatorCodes.CurrentCodeFor(setup.SharedKey), TestToken);

        using var afterConfirming = await client.PostAsJsonAsync(
            $"{AuthRoute}/refresh", new RefreshAccessTokenRequest(callersTokens.RefreshToken), TestToken);
        afterConfirming.StatusCode.ShouldBe(HttpStatusCode.OK);

        using var otherRefreshed = await otherDevice.PostAsJsonAsync(
            $"{AuthRoute}/refresh", new RefreshAccessTokenRequest(otherTokens.RefreshToken), TestToken);
        otherRefreshed.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await ApiJson.ReadProblemAsync(otherRefreshed, TestToken)).Code.ShouldBe("auth.refreshToken.invalid");
    }
}

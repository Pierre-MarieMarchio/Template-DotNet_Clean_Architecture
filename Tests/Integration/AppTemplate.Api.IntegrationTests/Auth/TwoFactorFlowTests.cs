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
/// The two-factor lifecycle end to end, against the real endpoints and the real database — the proof
/// the unit tests around <c>ITwoFactorEnrollment</c> and <c>ITwoFactorChallenge</c> cannot give on
/// their own, because each of those tests the adapter against a substituted store, never the whole
/// pipeline a caller actually walks through.
/// <para>
/// Login and the second step are always exercised from a freshly created client rather than the one
/// enrollment used: both are anonymous, and reusing the enrollment client would spend its
/// <see cref="AppTemplate.Api.Common.Security.RateLimitingExtensions.Authentication"/> budget down to
/// nothing across a single test.
/// </para>
/// </summary>
public sealed class TwoFactorFlowTests(ApiFixture fixture) : IntegrationTestBase(fixture)
{
    /// <summary>
    /// Setup, confirm, then a full two-step login with a code computed the same way a real
    /// authenticator app would compute it.
    /// </summary>
    [Fact]
    public async Task TheFullTwoStepLoginFlow_WorksEndToEndWithAnAuthenticatorCode()
    {
        var (client, user, session) = await SignInAsync();
        var (sharedKey, _) = await TwoFactorTestSupport.EnableTwoFactorAsync(client, user, session, TestToken);

        // Confirming rotated the stamp again and revoked every refresh token for the account: nothing
        // captured above still works, and login for this address now stops at a challenge.
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

    /// <summary>
    /// A wrong code against a live challenge is refused with the same error an unknown or expired
    /// challenge gets — see <c>AuthErrors.InvalidTwoFactorChallenge</c> — and the challenge survives
    /// the attempt, so a mistyped code does not force the caller back through <c>/login</c>.
    /// </summary>
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

    /// <summary>
    /// A recovery code completes the second step exactly like an authenticator code does, and — the
    /// reason ten of them exist rather than one reusable value — the same code cannot complete a
    /// second login.
    /// </summary>
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

        // A fresh challenge, same recovery code: it is single-use, not tied to one challenge.
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

    /// <summary>
    /// Disabling requires the current password and turns the challenge back off: after it, the same
    /// account logs in with a token pair again, with no second step.
    /// </summary>
    [Fact]
    public async Task DisablingTwoFactor_RestoresAOneStepLogin()
    {
        var (client, user, session) = await SignInAsync();
        var (sharedKey, _) = await TwoFactorTestSupport.EnableTwoFactorAsync(client, user, session, TestToken);

        // Confirming revoked every refresh token; sign back in the long way before disabling.
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

    /// <summary>
    /// A wrong current password neither disables the second factor nor spends anything a right
    /// password would have.
    /// </summary>
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

    /// <summary>
    /// The gap this repository was asked to close. Arming the second factor revokes every other
    /// session on the account exactly as disarming it does — see
    /// <see cref="DisablingTwoFactor_RestoresAOneStepLogin"/> — so a caller holding nothing but a
    /// stolen access token must not be able to do it on a code it produced itself, without ever
    /// proving the account's password.
    /// </summary>
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

        // Nothing was consumed by the refusal above: a login for this address still completes in one
        // step, exactly as it would have if enrollment had never been confirmed at all.
        using var loginResponse = await CreateClient().PostAsJsonAsync(
            $"{AuthRoute}/login",
            new LoginRequest(user.Email, user.Password),
            TestToken);
        loginResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await ApiJson.ReadAsync<LoginResponse>(loginResponse, TestToken))
            .ShouldBeOfType<LoginResponse.Authenticated>();

        // The same pending secret, the right password and the right code now confirm it — proving
        // the refusal above cost nothing a legitimate retry would need.
        using var rightPasswordResponse = await client.PostAsJsonAsync(
            $"{AuthRoute}/two-factor/confirm",
            new ConfirmTwoFactorSetupRequest(user.Password, AuthenticatorCodes.CurrentCodeFor(setup.SharedKey)),
            TestToken);
        rightPasswordResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await ApiJson.ReadAsync<ConfirmTwoFactorSetupResponse>(rightPasswordResponse, TestToken))
            .RecoveryCodes.ShouldNotBeEmpty();
    }
}

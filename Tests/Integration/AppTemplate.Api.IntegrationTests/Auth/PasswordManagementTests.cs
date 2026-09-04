using System.Net;
using System.Net.Http.Json;
using AppTemplate.Api.Features.Auth.Contracts.Requests;
using AppTemplate.Api.IntegrationTests.Infrastructure;
using Shouldly;
using Xunit;

namespace AppTemplate.Api.IntegrationTests.Auth;

/// <summary>
/// End to end coverage of the three password endpoints that had none:
/// <c>change-password</c>, <c>forgot-password</c> and <c>reset-password</c>.
/// </summary>
public sealed class PasswordManagementTests(ApiFixture fixture) : IntegrationTestBase(fixture)
{
    #region change-password

    [Fact]
    public async Task ChangePassword_WithTheCorrectCurrentPassword_SwapsWhichPasswordSignsIn()
    {
        var (client, user, _) = await SignInAsync();

        using var changed = await client.PostAsJsonAsync(
            $"{AuthRoute}/change-password",
            new ChangePasswordRequest(user.Password, "Rotated!Password2"),
            TestToken);
        changed.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        using var loginWithOld = await client.PostAsJsonAsync(
            $"{AuthRoute}/login", new LoginRequest(user.Email, user.Password), TestToken);
        loginWithOld.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        using var loginWithNew = await client.PostAsJsonAsync(
            $"{AuthRoute}/login", new LoginRequest(user.Email, "Rotated!Password2"), TestToken);
        loginWithNew.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ChangePassword_WithTheWrongCurrentPassword_IsRejectedAndLeavesItUnchanged()
    {
        var (client, user, _) = await SignInAsync();

        using var rejected = await client.PostAsJsonAsync(
            $"{AuthRoute}/change-password",
            new ChangePasswordRequest("Definitely-Not-It1", "Rotated!Password2"),
            TestToken);

        rejected.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var problem = await ApiJson.ReadProblemAsync(rejected, TestToken);
        problem.Code.ShouldBe("request.validationFailed");
        problem.Body.ShouldContain("currentPassword", Case.Sensitive);

        using var stillWorks = await client.PostAsJsonAsync(
            $"{AuthRoute}/login", new LoginRequest(user.Email, user.Password), TestToken);
        stillWorks.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    #endregion

    #region forgot-password

    /// <summary>
    /// The anti-enumeration property and the no-plaintext-logging one, together: a known and an
    /// unknown address must answer the same way, and neither may leave the known address readable in
    /// a log entry.
    /// </summary>
    [Fact]
    public async Task ForgotPassword_AnswersIdenticallyForAKnownAndAnUnknownAddress_AndLogsNoPlainAddress()
    {
        var client = CreateClient();
        var user = await RegisterConfirmedUserAsync(client);

        using var forKnown = await client.PostAsJsonAsync(
            $"{AuthRoute}/forgot-password", new ForgotPasswordRequest(user.Email), TestToken);
        using var forUnknown = await client.PostAsJsonAsync(
            $"{AuthRoute}/forgot-password",
            new ForgotPasswordRequest("nobody-at-all@integration.test"),
            TestToken);

        forKnown.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        forUnknown.StatusCode.ShouldBe(forKnown.StatusCode);

        // Proof the known branch actually did something: silence on both sides would satisfy the
        // assertions above without the endpoint having sent a single email.
        RequireLastEmailTo(user.Email).Subject.ShouldBe("Reset your password");
        Emails.LastTo("nobody-at-all@integration.test").ShouldBeNull();

        Fixture.Logs.Snapshot()
            .Where(record => record.Message.Contains(user.Email, StringComparison.Ordinal))
            .ShouldBeEmpty("The address must never reach a log on this anti-enumeration path.");
    }

    #endregion

    #region reset-password

    [Fact]
    public async Task ResetPassword_WithTheEmailedToken_ChangesThePasswordAndRevokesExistingRefreshTokens()
    {
        var (client, user, session) = await SignInAsync();

        using var requested = await client.PostAsJsonAsync(
            $"{AuthRoute}/forgot-password", new ForgotPasswordRequest(user.Email), TestToken);
        requested.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var (email, token) = ReadEmailLink(RequireLastEmailTo(user.Email));

        using var reset = await client.PostAsJsonAsync(
            $"{AuthRoute}/reset-password",
            new ResetPasswordRequest(email, token, "Rotated!Password2"),
            TestToken);
        reset.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        // The refresh token minted before the reset must not survive it.
        using var refreshed = await client.PostAsJsonAsync(
            $"{AuthRoute}/refresh",
            new RefreshAccessTokenRequest(session.Tokens.RefreshToken),
            TestToken);
        refreshed.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await ApiJson.ReadProblemAsync(refreshed, TestToken)).Code.ShouldBe("auth.refreshToken.invalid");

        using var loginWithOld = await client.PostAsJsonAsync(
            $"{AuthRoute}/login", new LoginRequest(user.Email, user.Password), TestToken);
        loginWithOld.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        using var loginWithNew = await client.PostAsJsonAsync(
            $"{AuthRoute}/login", new LoginRequest(user.Email, "Rotated!Password2"), TestToken);
        loginWithNew.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    /// <summary>
    /// The same collapse <c>ConfirmEmail</c> uses, for the same reason: telling an unknown address
    /// apart from a wrong token is exactly what a probe is trying to learn.
    /// </summary>
    [Fact]
    public async Task ResetPassword_AnUnknownAddressAndAWrongToken_AreIndistinguishable()
    {
        var client = CreateClient();
        var user = await RegisterConfirmedUserAsync(client, "reset-known");

        using var forgot = await client.PostAsJsonAsync(
            $"{AuthRoute}/forgot-password", new ForgotPasswordRequest(user.Email), TestToken);
        forgot.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var (email, token) = ReadEmailLink(RequireLastEmailTo(user.Email));

        using var wrongToken = await client.PostAsJsonAsync(
            $"{AuthRoute}/reset-password",
            new ResetPasswordRequest(email, token + "x", "Rotated!Password2"),
            TestToken);

        using var unknownAddress = await client.PostAsJsonAsync(
            $"{AuthRoute}/reset-password",
            new ResetPasswordRequest("nobody-at-all@integration.test", token, "Rotated!Password2"),
            TestToken);

        wrongToken.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        unknownAddress.StatusCode.ShouldBe(wrongToken.StatusCode);

        var first = await ApiJson.ReadProblemAsync(wrongToken, TestToken);
        var second = await ApiJson.ReadProblemAsync(unknownAddress, TestToken);

        first.BodyWithoutTraceId.ShouldBe(second.BodyWithoutTraceId);
    }

    #endregion
}

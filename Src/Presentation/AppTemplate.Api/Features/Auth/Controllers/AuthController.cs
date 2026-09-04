using AppTemplate.Api.Common.Caching;
using AppTemplate.Api.Common.Controllers;
using AppTemplate.Api.Common.Security;
using AppTemplate.Api.Features.Auth.Contracts.Requests;
using AppTemplate.Api.Features.Auth.Contracts.Responses;
using AppTemplate.Api.Features.Auth.Mapping;
using AppTemplate.Application.Features.Auth.UseCases.Commands.ChangePassword;
using AppTemplate.Application.Features.Auth.UseCases.Commands.ConfirmEmail;
using AppTemplate.Application.Features.Auth.UseCases.Commands.ConfirmEmailChange;
using AppTemplate.Application.Features.Auth.UseCases.Commands.ConfirmTwoFactorSetup;
using AppTemplate.Application.Features.Auth.UseCases.Commands.DisableTwoFactor;
using AppTemplate.Application.Features.Auth.UseCases.Commands.Login;
using AppTemplate.Application.Features.Auth.UseCases.Commands.Logout;
using AppTemplate.Application.Features.Auth.UseCases.Commands.LogoutEverywhere;
using AppTemplate.Application.Features.Auth.UseCases.Commands.RefreshAccessToken;
using AppTemplate.Application.Features.Auth.UseCases.Commands.Register;
using AppTemplate.Application.Features.Auth.UseCases.Commands.RequestEmailChange;
using AppTemplate.Application.Features.Auth.UseCases.Commands.RequestPasswordReset;
using AppTemplate.Application.Features.Auth.UseCases.Commands.ResendConfirmationEmail;
using AppTemplate.Application.Features.Auth.UseCases.Commands.ResetPassword;
using AppTemplate.Application.Features.Auth.UseCases.Commands.SetUpTwoFactor;
using AppTemplate.Application.Features.Auth.UseCases.Commands.VerifyTwoFactor;
using AppTemplate.Application.Features.Auth.UseCases.Queries.GetCurrentUser;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
// UseCases.Commands.ConfirmTwoFactorSetup declares an application-layer type by the same name.
using ConfirmTwoFactorSetupResponse = AppTemplate.Api.Features.Auth.Contracts.Responses.ConfirmTwoFactorSetupResponse;
// UseCases.Queries.GetCurrentUser declares an application-layer type by the same name.
using CurrentUserResponse = AppTemplate.Api.Features.Auth.Contracts.Responses.CurrentUserResponse;
// UseCases.Commands.Register declares an application-layer type by the same name.
using RegisterResponse = AppTemplate.Api.Features.Auth.Contracts.Responses.RegisterResponse;
// UseCases.Commands.SetUpTwoFactor declares an application-layer type by the same name.
using SetUpTwoFactorResponse = AppTemplate.Api.Features.Auth.Contracts.Responses.SetUpTwoFactorResponse;

namespace AppTemplate.Api.Features.Auth.Controllers;

/// <summary>
/// Authentication endpoints.
/// </summary>
/// <remarks>
/// The application's fallback policy requires authentication, so most of this surface opts out with
/// <c>[AllowAnonymous]</c> — action by action, never on the controller. <c>AllowAnonymous</c> short
/// circuits authorisation wherever it is found in an endpoint's metadata, so one declared on the
/// class would silently defeat the <c>[Authorize]</c> on <see cref="GetCurrentUser"/> and
/// <see cref="ChangePassword"/> and serve the caller's own profile to anyone.
/// <para>
/// The refresh token is returned in the response body rather than an <c>HttpOnly</c> cookie. That
/// suits every client type and carries no CSRF surface. For a browser-only SPA an
/// <c>HttpOnly; Secure; SameSite</c> cookie is the stronger choice against XSS: set it here and
/// drop the field from the response instead of serialising both.
/// </para>
/// <para>
/// Statuses are declared action by action. 409 belongs to sign-up and to
/// <see cref="SetUpTwoFactor"/>, where a taken email/user name and an already-armed second factor are
/// the real conflicting-state outcomes; no other action here has one. 401 belongs to the token
/// endpoints — <see cref="Login"/>, <see cref="LoginWithTwoFactor"/>, <see cref="Refresh"/> — where
/// bad credentials, an invalid challenge or a spent refresh token are the expected refusal, and to
/// every <c>[Authorize]</c> action, where it means the caller's token is missing or no longer valid.
/// </para>
/// <para>
/// The tight <see cref="RateLimitingPolicies.Authentication"/> budget is declared on each action that
/// handles a credential — a password, a TOTP code, a recovery code — and on none that does not.
/// <see cref="GetCurrentUser"/> and <see cref="LogoutEverywhere"/> are the exceptions and stay on the
/// global limiter: reading one's own profile, or clearing one's own sessions, is not an attempt at a
/// credential, and putting either on the credential budget would let a client that polls its profile
/// or cleans up its sessions spend the allowance that exists to slow brute force down.
/// </para>
/// <para>
/// Responses carrying a token, a two-factor shared key or a set of recovery codes are
/// <c>[NoStore]</c>: RFC 6749 §5.1 forbids any cache from storing an OAuth-style credential, and the
/// same reasoning covers a secret that is just as capable of signing in on its own.
/// </para>
/// </remarks>
[Route("api/v{version:apiVersion}/auth")]
[Asp.Versioning.ApiVersion("1.0")]
public sealed class AuthController(
    IRegisterUseCase register,
    ILoginUseCase login,
    IRefreshAccessTokenUseCase refreshAccessToken,
    IConfirmEmailUseCase confirmEmail,
    IResendConfirmationEmailUseCase resendConfirmationEmail,
    ILogoutUseCase logout,
    ILogoutEverywhereUseCase logoutEverywhere,
    IGetCurrentUserUseCase getCurrentUser,
    IChangePasswordUseCase changePassword,
    IRequestEmailChangeUseCase requestEmailChange,
    IConfirmEmailChangeUseCase confirmEmailChange,
    IRequestPasswordResetUseCase requestPasswordReset,
    IResetPasswordUseCase resetPassword,
    ISetUpTwoFactorUseCase setUpTwoFactor,
    IConfirmTwoFactorSetupUseCase confirmTwoFactorSetup,
    IDisableTwoFactorUseCase disableTwoFactor,
    IVerifyTwoFactorUseCase verifyTwoFactor) : ApiControllerBase
{
    /// <summary>Creates an account and sends a confirmation email.</summary>
    [HttpPost("register")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitingPolicies.Authentication)]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(RegisterResponse))]
    [ProducesResponseType(StatusCodes.Status400BadRequest, Type = typeof(ValidationProblemDetails))]
    [ProducesResponseType(StatusCodes.Status409Conflict, Type = typeof(ProblemDetails))]
    public async Task<ActionResult<RegisterResponse>> Register(
        [FromBody] RegisterRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var command = new RegisterCommand(request.UserName, request.Email, request.Password);

        return OkOrProblem(AuthMapping.ToRegisterResponse(await register.ExecuteAsync(command, cancellationToken)));
    }

    /// <summary>Exchanges credentials for an access token and a refresh token.</summary>
    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitingPolicies.Authentication)]
    [NoStore]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(LoginResponse))]
    [ProducesResponseType(StatusCodes.Status400BadRequest, Type = typeof(ValidationProblemDetails))]
    [ProducesResponseType(StatusCodes.Status401Unauthorized, Type = typeof(ProblemDetails))]
    public async Task<ActionResult<LoginResponse>> Login(
        [FromBody] LoginRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var command = new LoginCommand(request.Email, request.Password);

        return OkOrProblem(AuthMapping.ToLoginResponse(await login.ExecuteAsync(command, cancellationToken)));
    }

    /// <summary>
    /// The second step of a two-step login: exchanges a challenge token from
    /// <see cref="Login"/>'s <c>twoFactorRequired</c> response, plus a code, for a token pair. The
    /// code is either the authenticator app's current six digits or one of the recovery codes issued
    /// at enrollment.
    /// </summary>
    /// <remarks>
    /// Answers with the same <see cref="LoginResponse"/> shape as <see cref="Login"/> — always the
    /// <c>authenticated</c> branch here, since a second <c>twoFactorRequired</c> is not a thing this
    /// step can produce — so a client that already parses one parses the other for free.
    /// </remarks>
    [HttpPost("login/two-factor")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitingPolicies.Authentication)]
    [NoStore]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(LoginResponse))]
    [ProducesResponseType(StatusCodes.Status400BadRequest, Type = typeof(ValidationProblemDetails))]
    [ProducesResponseType(StatusCodes.Status401Unauthorized, Type = typeof(ProblemDetails))]
    public async Task<ActionResult<LoginResponse>> LoginWithTwoFactor(
        [FromBody] VerifyTwoFactorRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var command = new VerifyTwoFactorCommand(request.ChallengeToken, request.Code);

        return OkOrProblem(
            AuthMapping.ToLoginResponse(await verifyTwoFactor.ExecuteAsync(command, cancellationToken)));
    }

    /// <summary>
    /// Exchanges a refresh token for a new pair. The presented token is always revoked; replaying
    /// one that was already used revokes the whole family for that user.
    /// </summary>
    [HttpPost("refresh")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitingPolicies.Authentication)]
    [NoStore]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(TokenResponse))]
    [ProducesResponseType(StatusCodes.Status400BadRequest, Type = typeof(ValidationProblemDetails))]
    [ProducesResponseType(StatusCodes.Status401Unauthorized, Type = typeof(ProblemDetails))]
    public async Task<ActionResult<TokenResponse>> Refresh(
        [FromBody] RefreshAccessTokenRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var command = new RefreshAccessTokenCommand(request.RefreshToken);

        return OkOrProblem(
            AuthMapping.ToTokenResponse(await refreshAccessToken.ExecuteAsync(command, cancellationToken)));
    }

    /// <summary>
    /// Confirms an email address. This is a POST, not a GET: the single-use token must not land in
    /// server access logs, browser history or a <c>Referer</c> header.
    /// </summary>
    [HttpPost("confirm-email")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitingPolicies.Authentication)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest, Type = typeof(ValidationProblemDetails))]
    public async Task<ActionResult> ConfirmEmail(
        [FromBody] ConfirmEmailRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var command = new ConfirmEmailCommand(request.Email, request.Token);

        return NoContentOrProblem(await confirmEmail.ExecuteAsync(command, cancellationToken));
    }

    /// <summary>Re-sends the confirmation email. Exists because registration is not atomic:
    /// the account is committed before delivery, so a mail failure must be recoverable.</summary>
    /// <remarks>204 for every well-formed address, known or not, so the endpoint cannot be used to
    /// enumerate accounts.</remarks>
    [HttpPost("resend-confirmation-email")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitingPolicies.Authentication)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest, Type = typeof(ValidationProblemDetails))]
    public async Task<ActionResult> ResendConfirmationEmail(
        [FromBody] ResendConfirmationEmailRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var command = new ResendConfirmationEmailCommand(request.Email);

        return NoContentOrProblem(await resendConfirmationEmail.ExecuteAsync(command, cancellationToken));
    }

    /// <summary>Revokes a refresh token, ending the session it belongs to.</summary>
    [HttpPost("logout")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitingPolicies.Authentication)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest, Type = typeof(ValidationProblemDetails))]
    public async Task<ActionResult> Logout(
        [FromBody] LogoutRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var command = new LogoutCommand(request.RefreshToken);

        return NoContentOrProblem(await logout.ExecuteAsync(command, cancellationToken));
    }

    /// <summary>Revokes every refresh token grant belonging to the caller, ending all of their sessions.</summary>
    /// <remarks>
    /// 204: there is no resource to render, and a count of revoked grants would tell the caller about
    /// other sessions it still has open without giving it anything to act on.
    /// </remarks>
    [HttpPost("logout-all")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized, Type = typeof(ProblemDetails))]
    public async Task<ActionResult> LogoutEverywhere(CancellationToken cancellationToken) =>
        NoContentOrProblem(await logoutEverywhere.ExecuteAsync(cancellationToken));

    /// <summary>The authenticated caller's own profile. Takes no input: the identity is the token's.</summary>
    /// <remarks>
    /// One of two actions here without <see cref="RateLimitingPolicies.Authentication"/> — see
    /// <see cref="LogoutEverywhere"/> for the other — so it falls to the global limiter. A profile
    /// read is not an attempt at a credential, and a client that polls this — a session check on
    /// every app start — must not be spending the allowance that exists to slow brute force down. It
    /// is also the only endpoint that publishes an account id.
    /// </remarks>
    [HttpGet("me")]
    [HttpHead("me")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(CurrentUserResponse))]
    [ProducesResponseType(StatusCodes.Status401Unauthorized, Type = typeof(ProblemDetails))]
    public async Task<ActionResult<CurrentUserResponse>> GetCurrentUser(CancellationToken cancellationToken) =>
        OkOrProblem(AuthMapping.ToCurrentUserResponse(await getCurrentUser.ExecuteAsync(cancellationToken)));

    /// <summary>
    /// Replaces the caller's password. The current one is presented again as proof that the session
    /// is not a stolen token.
    /// </summary>
    [HttpPost("change-password")]
    [Authorize]
    [EnableRateLimiting(RateLimitingPolicies.Authentication)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest, Type = typeof(ValidationProblemDetails))]
    [ProducesResponseType(StatusCodes.Status401Unauthorized, Type = typeof(ProblemDetails))]
    public async Task<ActionResult> ChangePassword(
        [FromBody] ChangePasswordRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var command = new ChangePasswordCommand(request.CurrentPassword, request.NewPassword);

        return NoContentOrProblem(await changePassword.ExecuteAsync(command, cancellationToken));
    }

    /// <summary>
    /// Begins two-factor enrollment: provisions a shared key if none is pending yet, and returns it
    /// either as a raw string or as an <c>otpauth://</c> URI for a QR code. Arms nothing on its own —
    /// <see cref="ConfirmTwoFactorSetup"/> is what actually turns two-factor sign-in on, once the
    /// caller proves it can produce a code from what this returned.
    /// </summary>
    [HttpPost("two-factor/setup")]
    [Authorize]
    [EnableRateLimiting(RateLimitingPolicies.Authentication)]
    [NoStore]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(SetUpTwoFactorResponse))]
    [ProducesResponseType(StatusCodes.Status401Unauthorized, Type = typeof(ProblemDetails))]
    [ProducesResponseType(StatusCodes.Status409Conflict, Type = typeof(ProblemDetails))]
    public async Task<ActionResult<SetUpTwoFactorResponse>> SetUpTwoFactor(CancellationToken cancellationToken) =>
        OkOrProblem(AuthMapping.ToSetUpTwoFactorResponse(await setUpTwoFactor.ExecuteAsync(cancellationToken)));

    /// <summary>
    /// Confirms enrollment with a first code and turns two-factor sign-in on. Returns ten recovery
    /// codes, shown once: losing them along with the authenticator app is losing the account.
    /// </summary>
    [HttpPost("two-factor/confirm")]
    [Authorize]
    [EnableRateLimiting(RateLimitingPolicies.Authentication)]
    [NoStore]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(ConfirmTwoFactorSetupResponse))]
    [ProducesResponseType(StatusCodes.Status400BadRequest, Type = typeof(ValidationProblemDetails))]
    [ProducesResponseType(StatusCodes.Status401Unauthorized, Type = typeof(ProblemDetails))]
    public async Task<ActionResult<ConfirmTwoFactorSetupResponse>> ConfirmTwoFactorSetup(
        [FromBody] ConfirmTwoFactorSetupRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var command = new ConfirmTwoFactorSetupCommand(request.Code);

        return OkOrProblem(
            AuthMapping.ToConfirmTwoFactorSetupResponse(await confirmTwoFactorSetup.ExecuteAsync(command, cancellationToken)));
    }

    /// <summary>
    /// Turns two-factor sign-in back off. The current password is presented again, for the reason
    /// <see cref="ChangePassword"/> gives: a stolen session alone must not be able to strip the
    /// account's second factor.
    /// </summary>
    [HttpPost("two-factor/disable")]
    [Authorize]
    [EnableRateLimiting(RateLimitingPolicies.Authentication)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest, Type = typeof(ValidationProblemDetails))]
    [ProducesResponseType(StatusCodes.Status401Unauthorized, Type = typeof(ProblemDetails))]
    public async Task<ActionResult> DisableTwoFactor(
        [FromBody] DisableTwoFactorRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var command = new DisableTwoFactorCommand(request.CurrentPassword);

        return NoContentOrProblem(await disableTwoFactor.ExecuteAsync(command, cancellationToken));
    }

    /// <summary>
    /// Starts a change of the caller's email address by sending a single-use token to the new
    /// address. The current password is presented again, for the reason <see cref="ChangePassword"/>
    /// gives: a stolen session alone must not be able to move the account to an address the attacker
    /// controls.
    /// </summary>
    /// <remarks>
    /// 204 whether or not the new address is already registered: the response must not tell the
    /// caller which addresses exist.
    /// </remarks>
    [HttpPost("change-email")]
    [Authorize]
    [EnableRateLimiting(RateLimitingPolicies.Authentication)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest, Type = typeof(ValidationProblemDetails))]
    [ProducesResponseType(StatusCodes.Status401Unauthorized, Type = typeof(ProblemDetails))]
    public async Task<ActionResult> RequestEmailChange(
        [FromBody] RequestEmailChangeRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var command = new RequestEmailChangeCommand(request.CurrentPassword, request.NewEmail);

        return NoContentOrProblem(await requestEmailChange.ExecuteAsync(command, cancellationToken));
    }

    /// <summary>
    /// Confirms a pending email change from the token mailed to the new address. This is a POST, not
    /// a GET, for the reason <see cref="ConfirmEmail"/> gives. Authenticated by the same access token
    /// as <see cref="RequestEmailChange"/>: the new address is not on file until this call succeeds,
    /// so there is nothing to look the token up by except the caller's own identity.
    /// </summary>
    [HttpPost("confirm-email-change")]
    [Authorize]
    [EnableRateLimiting(RateLimitingPolicies.Authentication)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest, Type = typeof(ValidationProblemDetails))]
    [ProducesResponseType(StatusCodes.Status401Unauthorized, Type = typeof(ProblemDetails))]
    public async Task<ActionResult> ConfirmEmailChange(
        [FromBody] ConfirmEmailChangeRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var command = new ConfirmEmailChangeCommand(request.NewEmail, request.Token);

        return NoContentOrProblem(await confirmEmailChange.ExecuteAsync(command, cancellationToken));
    }

    /// <summary>Starts a password reset by emailing a single-use token.</summary>
    /// <remarks>
    /// 204 for every well-formed address, whether it names an account or not, and nothing about the
    /// address is logged: the response must not tell a caller which addresses are registered.
    /// </remarks>
    [HttpPost("forgot-password")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitingPolicies.Authentication)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest, Type = typeof(ValidationProblemDetails))]
    public async Task<ActionResult> RequestPasswordReset(
        [FromBody] ForgotPasswordRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var command = new RequestPasswordResetCommand(request.Email);

        return NoContentOrProblem(await requestPasswordReset.ExecuteAsync(command, cancellationToken));
    }

    /// <summary>
    /// Sets a new password from a reset token. This is a POST, not a GET: the single-use token must
    /// not land in server access logs, browser history or a <c>Referer</c> header.
    /// </summary>
    [HttpPost("reset-password")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitingPolicies.Authentication)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest, Type = typeof(ValidationProblemDetails))]
    public async Task<ActionResult> ResetPassword(
        [FromBody] ResetPasswordRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var command = new ResetPasswordCommand(request.Email, request.Token, request.NewPassword);

        return NoContentOrProblem(await resetPassword.ExecuteAsync(command, cancellationToken));
    }
}

using AppTemplate.Api.Common.Caching;
using AppTemplate.Api.Common.Controllers;
using AppTemplate.Api.Common.Security;
using AppTemplate.Api.Features.Auth.Contracts.Requests;
using AppTemplate.Api.Features.Auth.Contracts.Responses;
using AppTemplate.Api.Features.Auth.Mapping;
using AppTemplate.Application.Features.Auth.UseCases.Commands.ChangePassword;
using AppTemplate.Application.Features.Auth.UseCases.Commands.ConfirmEmail;
using AppTemplate.Application.Features.Auth.UseCases.Commands.Login;
using AppTemplate.Application.Features.Auth.UseCases.Commands.Logout;
using AppTemplate.Application.Features.Auth.UseCases.Commands.LogoutEverywhere;
using AppTemplate.Application.Features.Auth.UseCases.Commands.RefreshAccessToken;
using AppTemplate.Application.Features.Auth.UseCases.Commands.Register;
using AppTemplate.Application.Features.Auth.UseCases.Commands.RequestPasswordReset;
using AppTemplate.Application.Features.Auth.UseCases.Commands.ResendConfirmationEmail;
using AppTemplate.Application.Features.Auth.UseCases.Commands.ResetPassword;
using AppTemplate.Application.Features.Auth.UseCases.Queries.GetCurrentUser;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

// UseCases.Queries.GetCurrentUser declares an application-layer type by the same name.
using CurrentUserResponse = AppTemplate.Api.Features.Auth.Contracts.Responses.CurrentUserResponse;

// UseCases.Commands.Register declares an application-layer type by the same name.
using RegisterResponse = AppTemplate.Api.Features.Auth.Contracts.Responses.RegisterResponse;

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
/// Statuses are declared action by action. 409 belongs to sign-up alone, where a taken email or
/// user name is a real outcome; no other action here has conflicting state to report. 401 belongs to
/// the two token endpoints, where bad credentials or a spent refresh token are the expected refusal,
/// and to the two authenticated actions.
/// </para>
/// <para>
/// The tight <see cref="RateLimitingPolicies.Authentication"/> budget is declared on each action that
/// handles a credential, and on none that does not. <see cref="GetCurrentUser"/> and
/// <see cref="LogoutEverywhere"/> are the exceptions and stay on the global limiter: reading one's own
/// profile, or clearing one's own sessions, is not an attempt at a credential, and putting either on
/// the credential budget would let a client that polls its profile or cleans up its sessions spend the
/// allowance that exists to slow brute force down.
/// </para>
/// <para>
/// Responses carrying a token are <c>[NoStore]</c>: RFC 6749 §5.1 forbids any cache from storing
/// them.
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
    IRequestPasswordResetUseCase requestPasswordReset,
    IResetPasswordUseCase resetPassword) : ApiControllerBase
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

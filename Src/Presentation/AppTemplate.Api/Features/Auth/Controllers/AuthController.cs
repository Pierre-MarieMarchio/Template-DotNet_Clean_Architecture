using AppTemplate.Api.Common.Controllers;
using AppTemplate.Api.Common.Security;
using AppTemplate.Application.Features.Auth.Dtos;
using AppTemplate.Application.Features.Auth.UseCases.Commands;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace AppTemplate.Api.Features.Auth.Controllers;

/// <summary>
/// Authentication endpoints.
/// </summary>
/// <remarks>
/// Every action here is explicitly <c>[AllowAnonymous]</c> because the application's fallback
/// policy requires authentication — the opt-out is visible instead of being the default.
/// <para>
/// The refresh token is returned in the response body rather than an <c>HttpOnly</c> cookie. That
/// suits every client type and carries no CSRF surface. For a browser-only SPA an
/// <c>HttpOnly; Secure; SameSite</c> cookie is the stronger choice against XSS: set it here and
/// drop the field from the response instead of serialising both.
/// </para>
/// <para>
/// Statuses are declared action by action. 409 belongs to sign-up alone, where a taken email or
/// user name is a real outcome; the other five have no conflicting state to report. 429 is on all
/// of them — these endpoints carry their own rate-limiting policy on top of the global one.
/// </para>
/// </remarks>
[Route("api/v{version:apiVersion}/auth")]
[Asp.Versioning.ApiVersion("1.0")]
[AllowAnonymous]
[EnableRateLimiting(RateLimitingPolicies.Authentication)]
public sealed class AuthController(
    IRegisterUseCase register,
    ILoginUseCase login,
    IRefreshAccessTokenUseCase refreshAccessToken,
    IConfirmEmailUseCase confirmEmail,
    IResendConfirmationEmailUseCase resendConfirmationEmail,
    ILogoutUseCase logout) : ApiControllerBase
{
    /// <summary>Creates an account and sends a confirmation email.</summary>
    [HttpPost("register")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(RegisterResponse))]
    [ProducesResponseType(StatusCodes.Status400BadRequest, Type = typeof(ProblemDetails))]
    [ProducesResponseType(StatusCodes.Status409Conflict, Type = typeof(ProblemDetails))]
    public async Task<ActionResult<RegisterResponse>> Register(
        [FromBody] RegisterRequest request,
        CancellationToken cancellationToken) =>
        OkOrProblem(await register.ExecuteAsync(request, cancellationToken));

    /// <summary>Exchanges credentials for an access token and a refresh token.</summary>
    [HttpPost("login")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(LoginResponse))]
    [ProducesResponseType(StatusCodes.Status400BadRequest, Type = typeof(ProblemDetails))]
    [ProducesResponseType(StatusCodes.Status401Unauthorized, Type = typeof(ProblemDetails))]
    public async Task<ActionResult<LoginResponse>> Login(
        [FromBody] LoginRequest request,
        CancellationToken cancellationToken) =>
        OkOrProblem(await login.ExecuteAsync(request, cancellationToken));

    /// <summary>
    /// Exchanges a refresh token for a new pair. The presented token is always revoked; replaying
    /// one that was already used revokes the whole family for that user.
    /// </summary>
    [HttpPost("refresh")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(RefreshAccessTokenResponse))]
    [ProducesResponseType(StatusCodes.Status400BadRequest, Type = typeof(ProblemDetails))]
    [ProducesResponseType(StatusCodes.Status401Unauthorized, Type = typeof(ProblemDetails))]
    public async Task<ActionResult<RefreshAccessTokenResponse>> Refresh(
        [FromBody] RefreshAccessTokenRequest request,
        CancellationToken cancellationToken) =>
        OkOrProblem(await refreshAccessToken.ExecuteAsync(request, cancellationToken));

    /// <summary>
    /// Confirms an email address. This is a POST, not a GET: the single-use token must not land in
    /// server access logs, browser history or a <c>Referer</c> header.
    /// </summary>
    [HttpPost("confirm-email")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest, Type = typeof(ProblemDetails))]
    public async Task<ActionResult> ConfirmEmail(
        [FromBody] ConfirmEmailRequest request,
        CancellationToken cancellationToken) =>
        NoContentOrProblem(await confirmEmail.ExecuteAsync(request, cancellationToken));

    /// <summary>Re-sends the confirmation email. Exists because registration is not atomic:
    /// the account is committed before delivery, so a mail failure must be recoverable.</summary>
    [HttpPost("resend-confirmation-email")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest, Type = typeof(ProblemDetails))]
    public async Task<ActionResult> ResendConfirmationEmail(
        [FromBody] ResendConfirmationEmailRequest request,
        CancellationToken cancellationToken) =>
        NoContentOrProblem(await resendConfirmationEmail.ExecuteAsync(request, cancellationToken));

    /// <summary>Revokes a refresh token, ending the session it belongs to.</summary>
    [HttpPost("logout")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest, Type = typeof(ProblemDetails))]
    public async Task<ActionResult> Logout(
        [FromBody] LogoutRequest request,
        CancellationToken cancellationToken) =>
        NoContentOrProblem(await logout.ExecuteAsync(request, cancellationToken));
}

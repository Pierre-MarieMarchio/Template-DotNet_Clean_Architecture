using AppTemplate.Api.Core.Common.Controllers;
using AppTemplate.Api.Core.Common.Security;
using AppTemplate.Api.Features.Auth.Contracts.Requests;
using AppTemplate.Application.Auth.Features.Auth.UseCases.Commands.RequestPasswordReset;
using AppTemplate.Application.Auth.Features.Auth.UseCases.Commands.ResetPassword;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
namespace AppTemplate.Api.Features.Auth.Controllers;

/// <summary>
/// Regaining access to an account whose password is lost, without being signed in to ask.
/// </summary>
/// <remarks>
/// <c>[AllowAnonymous]</c> goes action by action, never on a controller: it short circuits
/// authorisation wherever an endpoint's metadata carries it, so one on a class would defeat the
/// <c>[Authorize]</c> on the actions beside it and serve a caller's own profile to anyone.
/// <para>
/// The tight <see cref="RateLimitingExtensions.Authentication"/> budget goes on each action handling
/// a credential -- a password, a TOTP code, a recovery code -- and on no other, so polling an
/// endpoint that handles none cannot spend the allowance that slows brute force down.
/// </para>
/// <para>
/// Responses carrying a token, a two-factor shared key or recovery codes are <c>[NoStore]</c>: RFC
/// 6749 §5.1 forbids a cache from storing an OAuth-style credential.
/// </para>
/// <para>
/// Five classes under the one <c>auth</c> prefix, and the prefix is the surface: the eighteen actions
/// are divided between them, and no path depends on which class answers it.
/// </para>
/// </remarks>
[Route("api/v{version:apiVersion}/auth")]
[Asp.Versioning.ApiVersion("1.0")]
public sealed class PasswordRecoveryController(
    IRequestPasswordResetUseCase requestPasswordReset,
    IResetPasswordUseCase resetPassword) : ApiControllerBase
{
    /// <summary>Starts a password reset by emailing a single-use token.</summary>
    /// <remarks>
    /// 204 for every well-formed address, whether it names an account or not, and nothing about the
    /// address is logged: the response must not tell a caller which addresses are registered.
    /// </remarks>
    [HttpPost("forgot-password")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitingExtensions.Authentication)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest, Type = typeof(ValidationProblemDetails))]
    public async Task<ActionResult> RequestPasswordReset(
        [FromBody] RequestPasswordResetRequest request,
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
    [EnableRateLimiting(RateLimitingExtensions.Authentication)]
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

using AppTemplate.Api.Common.Controllers;
using AppTemplate.Api.Common.Security;
using AppTemplate.Api.Features.Auth.Contracts.Requests;
using AppTemplate.Application.Auth.Features.Accounts.UseCases.Commands.RequestPasswordReset;
using AppTemplate.Application.Auth.Features.Accounts.UseCases.Commands.ResetPassword;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
namespace AppTemplate.Api.Features.Auth.Controllers;

[Route("api/v{version:apiVersion}/auth")]
[Asp.Versioning.ApiVersion("1.0")]
public sealed class PasswordRecoveryController(
    IRequestPasswordResetUseCase requestPasswordReset,
    IResetPasswordUseCase resetPassword) : ApiControllerBase
{
    /// <summary>Starts a password reset by emailing a single-use token.</summary>
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

    /// <summary>Sets a new password from a reset token.</summary>
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

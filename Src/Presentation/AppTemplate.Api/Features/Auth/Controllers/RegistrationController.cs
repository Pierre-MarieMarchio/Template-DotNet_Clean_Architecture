using AppTemplate.Api.Core.Common.Controllers;
using AppTemplate.Api.Core.Common.Security;
using AppTemplate.Api.Features.Auth.Contracts.Requests;
using AppTemplate.Api.Features.Auth.Contracts.Responses;
using AppTemplate.Api.Features.Auth.Mapping;
using AppTemplate.Application.Auth.Features.Auth.UseCases.Commands.ConfirmEmail;
using AppTemplate.Application.Auth.Features.Auth.UseCases.Commands.Register;
using AppTemplate.Application.Auth.Features.Auth.UseCases.Commands.ResendConfirmationEmail;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace AppTemplate.Api.Features.Auth.Controllers;

/// <summary>
/// Creating an account, and proving the address it was created with.
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
public sealed class RegistrationController(
    IRegisterUseCase register,
    IConfirmEmailUseCase confirmEmail,
    IResendConfirmationEmailUseCase resendConfirmationEmail) : ApiControllerBase
{
    /// <summary>Creates an account and sends a confirmation email.</summary>
    [HttpPost("register")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitingExtensions.Authentication)]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(RegisterResponse))]
    [ProducesResponseType(StatusCodes.Status400BadRequest, Type = typeof(ValidationProblemDetails))]
    [ProducesResponseType(StatusCodes.Status409Conflict, Type = typeof(ProblemDetails))]
    public async Task<ActionResult<RegisterResponse>> Register(
        [FromBody] RegisterRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var command = new RegisterCommand(request.UserName, request.Email, request.Password);

        return OkOrProblem(AuthResponseMapping.ToRegisterResponse(await register.ExecuteAsync(command, cancellationToken)));
    }

    /// <summary>
    /// Confirms an email address. This is a POST, not a GET: the single-use token must not land in
    /// server access logs, browser history or a <c>Referer</c> header.
    /// </summary>
    [HttpPost("confirm-email")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitingExtensions.Authentication)]
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
    [EnableRateLimiting(RateLimitingExtensions.Authentication)]
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
}

using AppTemplate.Api.Common.Controllers;
using AppTemplate.Api.Common.Security;
using AppTemplate.Api.Features.Auth.Contracts.Requests;
using AppTemplate.Api.Features.Auth.Contracts.Responses;
using AppTemplate.Application.Auth.Features.Accounts.UseCases.Commands.ConfirmEmail;
using AppTemplate.Application.Auth.Features.Accounts.UseCases.Commands.Register;
using AppTemplate.Application.Auth.Features.Accounts.UseCases.Commands.ResendConfirmationEmail;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace AppTemplate.Api.Features.Auth.Controllers;

[Route("api/v{version:apiVersion}/auth")]
[Asp.Versioning.ApiVersion("1.0")]
public sealed class RegistrationController(
    IRegisterUseCase register,
    IConfirmEmailUseCase confirmEmail,
    IResendConfirmationEmailUseCase resendConfirmationEmail) : ApiControllerBase
{
    /// <summary>Requests an account for an address and mails the address what happened.</summary>
    [HttpPost("register")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitingExtensions.Authentication)]
    [ProducesResponseType(StatusCodes.Status202Accepted, Type = typeof(RegisterResponse))]
    [ProducesResponseType(StatusCodes.Status400BadRequest, Type = typeof(ValidationProblemDetails))]
    public async Task<ActionResult<RegisterResponse>> Register(
        [FromBody] RegisterRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var command = new RegisterCommand(request.Email, request.Password);

        return AcceptedOrProblem(await register.ExecuteAsync(command, cancellationToken), RegisterResponse.Pending);
    }

    /// <summary>Confirms an email address.</summary>
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

    /// <summary>Re-sends the confirmation email.</summary>
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

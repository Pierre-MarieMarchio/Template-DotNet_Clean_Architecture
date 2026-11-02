using AppTemplate.Api.Common.Caching;
using AppTemplate.Api.Common.Controllers;
using AppTemplate.Api.Common.Security;
using AppTemplate.Api.Features.Auth.Contracts.Mapping;
using AppTemplate.Api.Features.Auth.Contracts.Requests;
using AppTemplate.Api.Features.Auth.Contracts.Responses;
using AppTemplate.Application.Auth.Features.TwoFactor.UseCases.Commands.ConfirmTwoFactorSetup;
using AppTemplate.Application.Auth.Features.TwoFactor.UseCases.Commands.DisableTwoFactor;
using AppTemplate.Application.Auth.Features.TwoFactor.UseCases.Commands.SetUpTwoFactor;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace AppTemplate.Api.Features.Auth.Controllers;

[Route("api/v{version:apiVersion}/auth")]
[Asp.Versioning.ApiVersion("1.0")]
public sealed class TwoFactorController(
    ISetUpTwoFactorUseCase setUpTwoFactor,
    IConfirmTwoFactorSetupUseCase confirmTwoFactorSetup,
    IDisableTwoFactorUseCase disableTwoFactor) : ApiControllerBase
{
    /// <summary>
    /// Begins two-factor enrollment and returns the shared key, raw and as an <c>otpauth://</c>
    /// URI.
    /// </summary>
    [HttpPost("two-factor/setup")]
    [Authorize]
    [EnableRateLimiting(RateLimitingExtensions.Authentication)]
    [NoStore]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(SetUpTwoFactorResponse))]
    [ProducesResponseType(StatusCodes.Status400BadRequest, Type = typeof(ProblemDetails))]
    [ProducesResponseType(StatusCodes.Status401Unauthorized, Type = typeof(ProblemDetails))]
    [ProducesResponseType(StatusCodes.Status409Conflict, Type = typeof(ProblemDetails))]
    public async Task<ActionResult<SetUpTwoFactorResponse>> SetUpTwoFactor(CancellationToken cancellationToken) =>
        OkOrProblem(AuthResponseMapping.ToSetUpTwoFactorResponse(await setUpTwoFactor.ExecuteAsync(cancellationToken)));

    /// <summary>Confirms enrollment with a first code and turns two-factor sign-in on.</summary>
    [HttpPost("two-factor/confirm")]
    [Authorize]
    [EnableRateLimiting(RateLimitingExtensions.Authentication)]
    [NoStore]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(ConfirmTwoFactorSetupResponse))]
    [ProducesResponseType(StatusCodes.Status400BadRequest, Type = typeof(ValidationProblemDetails))]
    [ProducesResponseType(StatusCodes.Status401Unauthorized, Type = typeof(ProblemDetails))]
    [ProducesResponseType(StatusCodes.Status409Conflict, Type = typeof(ProblemDetails))]
    public async Task<ActionResult<ConfirmTwoFactorSetupResponse>> ConfirmTwoFactorSetup(
        [FromBody] ConfirmTwoFactorSetupRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var command = new ConfirmTwoFactorSetupCommand(request.CurrentPassword, request.Code);

        return OkOrProblem(
            AuthResponseMapping.ToConfirmTwoFactorSetupResponse(await confirmTwoFactorSetup.ExecuteAsync(command, cancellationToken)));
    }

    /// <summary>Turns two-factor sign-in back off.</summary>
    [HttpPost("two-factor/disable")]
    [Authorize]
    [EnableRateLimiting(RateLimitingExtensions.Authentication)]
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
}

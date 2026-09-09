using AppTemplate.Api.Core.Common.Caching;
using AppTemplate.Api.Core.Common.Controllers;
using AppTemplate.Api.Core.Common.Security;
using AppTemplate.Api.Features.Auth.Contracts.Requests;
using AppTemplate.Api.Features.Auth.Contracts.Responses;
using AppTemplate.Api.Features.Auth.Mapping;
using AppTemplate.Application.Auth.Features.Auth.UseCases.Commands.ConfirmTwoFactorSetup;
using AppTemplate.Application.Auth.Features.Auth.UseCases.Commands.DisableTwoFactor;
using AppTemplate.Application.Auth.Features.Auth.UseCases.Commands.SetUpTwoFactor;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace AppTemplate.Api.Features.Auth.Controllers;

/// <summary>
/// Turning the second factor on and off. Redeeming it is a sign-in, and lives with the sessions.
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
public sealed class TwoFactorController(
    ISetUpTwoFactorUseCase setUpTwoFactor,
    IConfirmTwoFactorSetupUseCase confirmTwoFactorSetup,
    IDisableTwoFactorUseCase disableTwoFactor) : ApiControllerBase
{
    /// <summary>
    /// Begins two-factor enrollment: provisions a shared key if none is pending yet, and returns it
    /// either as a raw string or as an <c>otpauth://</c> URI for a QR code. Arms nothing on its own —
    /// <see cref="ConfirmTwoFactorSetup"/> is what actually turns two-factor sign-in on, once the
    /// caller proves it can produce a code from what this returned.
    /// </summary>
    [HttpPost("two-factor/setup")]
    [Authorize]
    [EnableRateLimiting(RateLimitingExtensions.Authentication)]
    [NoStore]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(SetUpTwoFactorResponse))]
    [ProducesResponseType(StatusCodes.Status401Unauthorized, Type = typeof(ProblemDetails))]
    [ProducesResponseType(StatusCodes.Status409Conflict, Type = typeof(ProblemDetails))]
    public async Task<ActionResult<SetUpTwoFactorResponse>> SetUpTwoFactor(CancellationToken cancellationToken) =>
        OkOrProblem(AuthResponseMapping.ToSetUpTwoFactorResponse(await setUpTwoFactor.ExecuteAsync(cancellationToken)));

    /// <summary>
    /// Confirms enrollment with a first code and turns two-factor sign-in on. Returns ten recovery
    /// codes, shown once: losing them along with the authenticator app is losing the account. The
    /// current password is presented again, for the reason <see cref="DisableTwoFactor"/> gives: a
    /// stolen session alone must not be able to arm the account's second factor any more than it can
    /// strip one — confirming revokes every other session exactly as disabling does, so proving the
    /// password matters just as much here.
    /// </summary>
    [HttpPost("two-factor/confirm")]
    [Authorize]
    [EnableRateLimiting(RateLimitingExtensions.Authentication)]
    [NoStore]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(ConfirmTwoFactorSetupResponse))]
    [ProducesResponseType(StatusCodes.Status400BadRequest, Type = typeof(ValidationProblemDetails))]
    [ProducesResponseType(StatusCodes.Status401Unauthorized, Type = typeof(ProblemDetails))]
    public async Task<ActionResult<ConfirmTwoFactorSetupResponse>> ConfirmTwoFactorSetup(
        [FromBody] ConfirmTwoFactorSetupRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var command = new ConfirmTwoFactorSetupCommand(request.CurrentPassword, request.Code);

        return OkOrProblem(
            AuthResponseMapping.ToConfirmTwoFactorSetupResponse(await confirmTwoFactorSetup.ExecuteAsync(command, cancellationToken)));
    }

    /// <summary>
    /// Turns two-factor sign-in back off. The current password is presented again, for the reason
    /// <see cref="AccountController.ChangePassword"/> gives: a stolen session alone must not be able
    /// to strip the
    /// account's second factor.
    /// </summary>
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

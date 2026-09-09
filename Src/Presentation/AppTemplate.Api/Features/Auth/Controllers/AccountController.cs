using AppTemplate.Api.Core.Common.Controllers;
using AppTemplate.Api.Core.Common.Security;
using AppTemplate.Api.Features.Auth.Contracts.Requests;
using AppTemplate.Api.Features.Auth.Contracts.Responses;
using AppTemplate.Api.Features.Auth.Mapping;
using AppTemplate.Application.Auth.Features.Auth.UseCases.Commands.ChangePassword;
using AppTemplate.Application.Auth.Features.Auth.UseCases.Commands.ConfirmEmailChange;
using AppTemplate.Application.Auth.Features.Auth.UseCases.Commands.RequestEmailChange;
using AppTemplate.Application.Auth.Features.Auth.UseCases.Queries.GetCurrentUser;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace AppTemplate.Api.Features.Auth.Controllers;

/// <summary>
/// What the authenticated caller can read and change about their own account.
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
public sealed class AccountController(
    IGetCurrentUserUseCase getCurrentUser,
    IChangePasswordUseCase changePassword,
    IRequestEmailChangeUseCase requestEmailChange,
    IConfirmEmailChangeUseCase confirmEmailChange) : ApiControllerBase
{
    /// <summary>The authenticated caller's own profile. Takes no input: the identity is the token's.</summary>
    /// <remarks>
    /// One of two actions here without <see cref="RateLimitingExtensions.Authentication"/> — see
    /// <see cref="SessionsController.LogoutEverywhere"/> for the other — so it falls to the global
    /// limiter. A profile
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
        OkOrProblem(AuthResponseMapping.ToCurrentUserResponse(await getCurrentUser.ExecuteAsync(cancellationToken)));

    /// <summary>
    /// Replaces the caller's password. The current one is presented again as proof that the session
    /// is not a stolen token.
    /// </summary>
    [HttpPost("change-password")]
    [Authorize]
    [EnableRateLimiting(RateLimitingExtensions.Authentication)]
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
    [EnableRateLimiting(RateLimitingExtensions.Authentication)]
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
    /// a GET, for the reason <see cref="RegistrationController.ConfirmEmail"/> gives. Authenticated by
    /// the same access token
    /// as <see cref="RequestEmailChange"/>: the new address is not on file until this call succeeds,
    /// so there is nothing to look the token up by except the caller's own identity.
    /// </summary>
    [HttpPost("confirm-email-change")]
    [Authorize]
    [EnableRateLimiting(RateLimitingExtensions.Authentication)]
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
}

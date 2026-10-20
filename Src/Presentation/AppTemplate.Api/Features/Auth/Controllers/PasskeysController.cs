using System.Buffers.Text;
//#if (browserSessions)
using System.ComponentModel.DataAnnotations;
//#endif
using AppTemplate.Api.Common.Caching;
using AppTemplate.Api.Common.Controllers;
using AppTemplate.Api.Common.Security;
//#if (browserSessions)
using AppTemplate.Api.Features.Auth.Composition;
//#endif
using AppTemplate.Api.Features.Auth.Contracts.Mapping;
using AppTemplate.Api.Features.Auth.Contracts.Requests;
using AppTemplate.Api.Features.Auth.Contracts.Responses;
using AppTemplate.Application.Auth.Features.Passkeys.Model.Errors;
using AppTemplate.Application.Auth.Features.Passkeys.UseCases.Commands.BeginPasskeyRegistration;
using AppTemplate.Application.Auth.Features.Passkeys.UseCases.Commands.BeginPasskeySignIn;
using AppTemplate.Application.Auth.Features.Passkeys.UseCases.Commands.CompletePasskeyRegistration;
using AppTemplate.Application.Auth.Features.Passkeys.UseCases.Commands.CompletePasskeySignIn;
using AppTemplate.Application.Auth.Features.Passkeys.UseCases.Commands.RemovePasskey;
using AppTemplate.Application.Auth.Features.Passkeys.UseCases.Queries.ListPasskeys;
using AppTemplate.Application.Core.Common.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace AppTemplate.Api.Features.Auth.Controllers;

[Route("api/v{version:apiVersion}/auth")]
[Asp.Versioning.ApiVersion("1.0")]
public sealed class PasskeysController(
    IBeginPasskeyRegistrationUseCase beginPasskeyRegistration,
    ICompletePasskeyRegistrationUseCase completePasskeyRegistration,
    IListPasskeysUseCase listPasskeys,
    IRemovePasskeyUseCase removePasskey,
    IBeginPasskeySignInUseCase beginPasskeySignIn,
    ICompletePasskeySignInUseCase completePasskeySignIn) : ApiControllerBase
{
    /// <summary>
    /// Starts a passkey registration: options for <c>navigator.credentials.create()</c> and the
    /// state to post back.
    /// </summary>
    [HttpPost("passkeys/register/options")]
    [Authorize]
    [EnableRateLimiting(RateLimitingExtensions.Authentication)]
    [NoStore]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(PasskeyRegistrationOptionsResponse))]
    [ProducesResponseType(StatusCodes.Status401Unauthorized, Type = typeof(ProblemDetails))]
    public async Task<ActionResult<PasskeyRegistrationOptionsResponse>> BeginPasskeyRegistration(
        CancellationToken cancellationToken) =>
        OkOrProblem(AuthResponseMapping.ToRegistrationOptionsResponse(
            await beginPasskeyRegistration.ExecuteAsync(cancellationToken)));

    /// <summary>
    /// Completes a passkey registration with the created credential, its state, an optional label
    /// and the current password.
    /// </summary>
    [HttpPost("passkeys/register")]
    [Authorize]
    [EnableRateLimiting(RateLimitingExtensions.Authentication)]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(PasskeyResponse))]
    [ProducesResponseType(StatusCodes.Status400BadRequest, Type = typeof(ValidationProblemDetails))]
    [ProducesResponseType(StatusCodes.Status401Unauthorized, Type = typeof(ProblemDetails))]
    [ProducesResponseType(StatusCodes.Status403Forbidden, Type = typeof(ProblemDetails))]
    public async Task<ActionResult<PasskeyResponse>> CompletePasskeyRegistration(
        [FromBody] CompletePasskeyRegistrationRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var command = new CompletePasskeyRegistrationCommand(
            request.Credential,
            request.RegistrationState,
            request.Name,
            request.CurrentPassword);

        return OkOrProblem(AuthResponseMapping.ToPasskeyResponse(
            await completePasskeyRegistration.ExecuteAsync(command, cancellationToken)));
    }

    /// <summary>Lists the passkeys registered on the caller's account, newest first.</summary>
    [HttpGet("passkeys")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(PasskeysResponse))]
    [ProducesResponseType(StatusCodes.Status401Unauthorized, Type = typeof(ProblemDetails))]
    public async Task<ActionResult<PasskeysResponse>> ListPasskeys(CancellationToken cancellationToken) =>
        OkOrProblem(AuthResponseMapping.ToPasskeysResponse(
            await listPasskeys.ExecuteAsync(cancellationToken)));

    /// <summary>Removes one of the caller's passkeys by the id the passkey list names.</summary>
    [HttpDelete("passkeys/{credentialId}")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized, Type = typeof(ProblemDetails))]
    [ProducesResponseType(StatusCodes.Status404NotFound, Type = typeof(ProblemDetails))]
    public async Task<ActionResult> RemovePasskey(string credentialId, CancellationToken cancellationToken)
    {
        byte[] credentialIdBytes;

        try
        {
            credentialIdBytes = Base64Url.DecodeFromChars(credentialId);
        }
        catch (FormatException)
        {
            return NoContentOrProblem(Result.Failure(PasskeysErrors.NoSuchPasskey));
        }

        var command = new RemovePasskeyCommand(credentialIdBytes);

        return NoContentOrProblem(await removePasskey.ExecuteAsync(command, cancellationToken));
    }

    /// <summary>
    /// Starts a passkey sign-in: options for <c>navigator.credentials.get()</c> and the state to
    /// post back.
    /// </summary>
    [HttpPost("passkeys/sign-in/options")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitingExtensions.Authentication)]
    [NoStore]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(PasskeySignInOptionsResponse))]
    public async Task<ActionResult<PasskeySignInOptionsResponse>> BeginPasskeySignIn(
        CancellationToken cancellationToken) =>
        OkOrProblem(AuthResponseMapping.ToSignInOptionsResponse(
            await beginPasskeySignIn.ExecuteAsync(cancellationToken)));

    /// <summary>Completes a passkey sign-in, exchanging the assertion for a token pair.</summary>
    [HttpPost("passkeys/sign-in")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitingExtensions.Authentication)]
    [NoStore]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(LoginResponse))]
    [ProducesResponseType(StatusCodes.Status400BadRequest, Type = typeof(ValidationProblemDetails))]
    [ProducesResponseType(StatusCodes.Status401Unauthorized, Type = typeof(ProblemDetails))]
    public async Task<ActionResult<LoginResponse>> CompletePasskeySignIn(
        [FromBody] PasskeySignInRequest request,
        //#if (browserSessions)
        [FromHeader(Name = BrowserSession.ModeHeader), RegularExpression(BrowserSession.ModePattern)]
        string? refreshTokenMode,
        //#endif
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var command = new CompletePasskeySignInCommand(request.Credential, request.AuthenticationState);
        var response = AuthResponseMapping.ToLoginResponse(
            await completePasskeySignIn.ExecuteAsync(command, cancellationToken));
        //#if (browserSessions)

        response = BrowserSession.Deliver(HttpContext, refreshTokenMode, response);
        //#endif

        return OkOrProblem(response);
    }
}

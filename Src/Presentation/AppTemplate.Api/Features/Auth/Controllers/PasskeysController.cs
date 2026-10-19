using System.Buffers.Text;
using AppTemplate.Api.Common.Caching;
using AppTemplate.Api.Common.Controllers;
using AppTemplate.Api.Common.Security;
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

/// <summary>Registering, listing, removing and signing in with passkeys — every endpoint of the
/// WebAuthn ceremonies, over the support ASP.NET Core Identity itself provides.</summary>
/// <remarks>
/// <c>[AllowAnonymous]</c> goes action by action, never on a controller: it short circuits
/// authorisation wherever an endpoint's metadata carries it, so one on a class would defeat the
/// <c>[Authorize]</c> on the actions beside it. The tight
/// <see cref="RateLimitingExtensions.Authentication"/> budget goes on the four ceremony steps and
/// no other, so the list and the removal — which handle no credential — cannot spend the
/// brute-force allowance. The two options responses are <c>[NoStore]</c>: each carries a challenge
/// this API minted for one ceremony, and the sign-in response carries a token pair, which RFC
/// 6749 §5.1 keeps out of any cache. The ceremony state that travels with a challenge is opaque
/// to the client and verified against what the client posts back — this API issues no cookie to
/// hold it. Six classes under the one <c>auth</c> prefix, and the prefix is the surface: no path
/// depends on which class answers it.
/// </remarks>
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
    /// <summary>The first step of registering a passkey: creation options for
    /// <c>navigator.credentials.create()</c>, and the state to post back with the credential that
    /// results. Arms nothing on its own — nothing is stored until
    /// <see cref="CompletePasskeyRegistration"/> has both parts.</summary>
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

    /// <summary>The second step of registering a passkey: the credential the client created, the
    /// state the options named, an optional label — and the current password, for the reason
    /// <see cref="TwoFactorController.ConfirmTwoFactorSetup"/> gives: a stolen session alone must not
    /// be able to arm a complete way in. An account with no password is held instead to an access
    /// token issued less than five minutes ago, answered
    /// <c>403 auth.account.reAuthenticationRequired</c> when the calling one is older.</summary>
    /// <remarks>Sessions are untouched: registering a credential changes what future sign-ins can present, not what the sessions already open are worth.</remarks>
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

    /// <summary>
    /// The passkeys registered on the caller's account, the newest first — the ids the removal
    /// route takes, so a client can offer "sign in with a passkey" next to the list of what is
    /// already armed.
    /// </summary>
    [HttpGet("passkeys")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(PasskeysResponse))]
    [ProducesResponseType(StatusCodes.Status401Unauthorized, Type = typeof(ProblemDetails))]
    public async Task<ActionResult<PasskeysResponse>> ListPasskeys(CancellationToken cancellationToken) =>
        OkOrProblem(AuthResponseMapping.ToPasskeysResponse(
            await listPasskeys.ExecuteAsync(cancellationToken)));

    /// <summary>Removes one of the caller's passkeys by the id the passkey list names. A passkey
    /// that belongs to somebody else answers 404, exactly as an unknown id does, so the route cannot
    /// be used to learn which credentials exist; one already removed gets the same 404. Every other
    /// session of the account ends with the removal, as with a password change.</summary>
    /// <remarks>An id that is not the base64url the list hands out gets the same 404: it names no
    /// passkey this account holds.</remarks>
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

    /// <summary>The first step of signing in with a passkey: request options for
    /// <c>navigator.credentials.get()</c>, and the state to post back with the assertion that
    /// results. No account is named and none is disclosed — the authenticator names one by offering
    /// a discoverable credential.</summary>
    [HttpPost("passkeys/sign-in/options")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitingExtensions.Authentication)]
    [NoStore]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(PasskeySignInOptionsResponse))]
    public async Task<ActionResult<PasskeySignInOptionsResponse>> BeginPasskeySignIn(
        CancellationToken cancellationToken) =>
        OkOrProblem(AuthResponseMapping.ToSignInOptionsResponse(
            await beginPasskeySignIn.ExecuteAsync(cancellationToken)));

    /// <summary>The second step of signing in with a passkey: the assertion the client obtained,
    /// exchanged for the same response shape as <c>login</c> — always the <c>authenticated</c>
    /// branch, since a user-verified passkey is the whole of a sign-in. One sign-in, one session,
    /// exactly as a password's.</summary>
    /// <remarks>One 401 for every refusal, so the route answers a guess with nothing it can act
    /// on. See <see cref="SessionsController.LoginWithTwoFactor"/> for the two-step shape a
    /// password sign-in takes instead.</remarks>
    [HttpPost("passkeys/sign-in")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitingExtensions.Authentication)]
    [NoStore]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(LoginResponse))]
    [ProducesResponseType(StatusCodes.Status400BadRequest, Type = typeof(ValidationProblemDetails))]
    [ProducesResponseType(StatusCodes.Status401Unauthorized, Type = typeof(ProblemDetails))]
    public async Task<ActionResult<LoginResponse>> CompletePasskeySignIn(
        [FromBody] PasskeySignInRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var command = new CompletePasskeySignInCommand(request.Credential, request.AuthenticationState);

        return OkOrProblem(AuthResponseMapping.ToLoginResponse(
            await completePasskeySignIn.ExecuteAsync(command, cancellationToken)));
    }
}

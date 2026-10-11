using AppTemplate.Api.Common.Caching;
using AppTemplate.Api.Common.Controllers;
using AppTemplate.Api.Common.Security;
using AppTemplate.Api.Features.Auth.Contracts.Requests;
using AppTemplate.Api.Features.Auth.Contracts.Responses;
using AppTemplate.Api.Features.Auth.Mapping;
using AppTemplate.Application.Auth.Features.Auth.UseCases.Commands.Login;
using AppTemplate.Application.Auth.Features.Auth.UseCases.Commands.Logout;
using AppTemplate.Application.Auth.Features.Auth.UseCases.Commands.LogoutEverywhere;
using AppTemplate.Application.Auth.Features.Auth.UseCases.Commands.RefreshAccessToken;
using AppTemplate.Application.Auth.Features.Auth.UseCases.Commands.RevokeSession;
using AppTemplate.Application.Auth.Features.Auth.UseCases.Commands.SignInWithExternalProvider;
using AppTemplate.Application.Auth.Features.Auth.UseCases.Commands.VerifyTwoFactor;
using AppTemplate.Application.Auth.Features.Auth.UseCases.Queries.ListSessions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace AppTemplate.Api.Features.Auth.Controllers;

/// <summary>Starting a session, extending it, and ending it -- every endpoint that mints or revokes a token.</summary>
/// <remarks>
/// <c>[AllowAnonymous]</c> goes action by action, never on a controller: it short circuits
/// authorisation wherever an endpoint's metadata carries it, so one on a class would defeat the
/// <c>[Authorize]</c> on the actions beside it. The tight
/// <see cref="RateLimitingExtensions.Authentication"/> budget goes on each action handling a
/// credential and on no other, so polling an endpoint that handles none cannot spend the
/// brute-force allowance. Responses carrying a token are <c>[NoStore]</c>: RFC 6749 §5.1 forbids
/// a cache from storing an OAuth-style credential. Six classes under the one <c>auth</c> prefix,
/// and the prefix is the surface: no path depends on which class answers it.
/// </remarks>
[Route("api/v{version:apiVersion}/auth")]
[Asp.Versioning.ApiVersion("1.0")]
public sealed class SessionsController(
    ILoginUseCase login,
    IVerifyTwoFactorUseCase verifyTwoFactor,
    ISignInWithExternalProviderUseCase signInWithExternalProvider,
    IRefreshAccessTokenUseCase refreshAccessToken,
    ILogoutUseCase logout,
    ILogoutEverywhereUseCase logoutEverywhere,
    IListSessionsUseCase listSessions,
    IRevokeSessionUseCase revokeSession) : ApiControllerBase
{
    /// <summary>Exchanges credentials for an access token and a refresh token.</summary>
    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitingExtensions.Authentication)]
    [NoStore]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(LoginResponse))]
    [ProducesResponseType(StatusCodes.Status400BadRequest, Type = typeof(ValidationProblemDetails))]
    [ProducesResponseType(StatusCodes.Status401Unauthorized, Type = typeof(ProblemDetails))]
    public async Task<ActionResult<LoginResponse>> Login(
        [FromBody] LoginRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var command = new LoginCommand(request.Email, request.Password);

        return OkOrProblem(AuthResponseMapping.ToLoginResponse(await login.ExecuteAsync(command, cancellationToken)));
    }

    /// <summary>The second step of a two-step login: exchanges a challenge token from
    /// <see cref="Login"/>'s <c>twoFactorRequired</c> response, plus a code, for a token pair — the
    /// authenticator app's current six digits or one of the enrollment's recovery codes.</summary>
    /// <remarks>Answers with the same <see cref="LoginResponse"/> shape as <see cref="Login"/> —
    /// always the <c>authenticated</c> branch, since a second <c>twoFactorRequired</c> is not a thing
    /// this step can produce.</remarks>
    [HttpPost("login/two-factor")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitingExtensions.Authentication)]
    [NoStore]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(LoginResponse))]
    [ProducesResponseType(StatusCodes.Status400BadRequest, Type = typeof(ValidationProblemDetails))]
    [ProducesResponseType(StatusCodes.Status401Unauthorized, Type = typeof(ProblemDetails))]
    public async Task<ActionResult<LoginResponse>> LoginWithTwoFactor(
        [FromBody] VerifyTwoFactorRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var command = new VerifyTwoFactorCommand(request.ChallengeToken, request.Code);

        return OkOrProblem(
            AuthResponseMapping.ToLoginResponse(await verifyTwoFactor.ExecuteAsync(command, cancellationToken)));
    }

    /// <summary>Exchanges an <c>id_token</c> the client obtained from an identity provider for this
    /// API's own token pair, creating or linking a local account on the way. This API issues no
    /// redirect and sets no cookie: the client runs the provider's
    /// authorisation-code-with-PKCE flow itself and posts only the result.</summary>
    /// <remarks>Answers with <see cref="ExternalLoginResponse"/> — <see cref="LoginResponse"/>'s
    /// shape plus one field, including the <c>twoFactorRequired</c> branch redeemed at
    /// <see cref="LoginWithTwoFactor"/>: a provider proves who the caller is, which is what a
    /// password proves and no more, so serving a token pair for an account with a second factor armed
    /// would make linking a provider the way to walk around it. One 401 for every refusal: anyone can
    /// obtain an <c>id_token</c> for an address they control, so a distinct answer for any of them
    /// turns this into a probe for which addresses are registered here; the provider is a body field
    /// rather than a route segment for the same reason
    /// (<see cref="SignInWithExternalProviderRequest"/>).</remarks>
    [HttpPost("login/external")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitingExtensions.Authentication)]
    [NoStore]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(ExternalLoginResponse))]
    [ProducesResponseType(StatusCodes.Status400BadRequest, Type = typeof(ValidationProblemDetails))]
    [ProducesResponseType(StatusCodes.Status401Unauthorized, Type = typeof(ProblemDetails))]
    public async Task<ActionResult<ExternalLoginResponse>> LoginWithExternalProvider(
        [FromBody] SignInWithExternalProviderRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var command = new SignInWithExternalProviderCommand(request.Provider, request.IdToken);

        return OkOrProblem(
            AuthResponseMapping.ToExternalLoginResponse(
                await signInWithExternalProvider.ExecuteAsync(command, cancellationToken)));
    }

    /// <summary>
    /// Exchanges a refresh token for a new pair. The presented token is always revoked; replaying
    /// one that was already used revokes the whole family for that user.
    /// </summary>
    [HttpPost("refresh")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitingExtensions.Authentication)]
    [NoStore]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(TokenResponse))]
    [ProducesResponseType(StatusCodes.Status400BadRequest, Type = typeof(ValidationProblemDetails))]
    [ProducesResponseType(StatusCodes.Status401Unauthorized, Type = typeof(ProblemDetails))]
    public async Task<ActionResult<TokenResponse>> Refresh(
        [FromBody] RefreshAccessTokenRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var command = new RefreshAccessTokenCommand(request.RefreshToken);

        return OkOrProblem(
            AuthResponseMapping.ToTokenResponse(await refreshAccessToken.ExecuteAsync(command, cancellationToken)));
    }

    /// <summary>Revokes a refresh token, ending the session it belongs to.</summary>
    [HttpPost("logout")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitingExtensions.Authentication)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest, Type = typeof(ValidationProblemDetails))]
    public async Task<ActionResult> Logout(
        [FromBody] LogoutRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var command = new LogoutCommand(request.RefreshToken);

        return NoContentOrProblem(await logout.ExecuteAsync(command, cancellationToken));
    }

    /// <summary>Revokes every refresh token grant belonging to the caller, ending all of their sessions.</summary>
    /// <remarks>
    /// 204: there is no resource to render, and a count of revoked grants would tell the caller about
    /// other sessions it still has open without giving it anything to act on.
    /// </remarks>
    [HttpPost("logout-all")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized, Type = typeof(ProblemDetails))]
    public async Task<ActionResult> LogoutEverywhere(CancellationToken cancellationToken) =>
        NoContentOrProblem(await logoutEverywhere.ExecuteAsync(cancellationToken));

    /// <summary>The caller's live sessions, most recently used first, the one this request is made
    /// from marked so a client can show "this device" and offer to end the others.</summary>
    /// <remarks>Takes no input and carries no credential, so it falls to the global limiter like
    /// <see cref="LogoutEverywhere"/> — a client refreshing this list on every app start must not
    /// spend the allowance that slows brute force down.</remarks>
    [HttpGet("sessions")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(SessionsResponse))]
    [ProducesResponseType(StatusCodes.Status401Unauthorized, Type = typeof(ProblemDetails))]
    public async Task<ActionResult<SessionsResponse>> ListSessions(CancellationToken cancellationToken) =>
        OkOrProblem(AuthResponseMapping.ToSessionsResponse(await listSessions.ExecuteAsync(cancellationToken)));

    /// <summary>Ends one of the caller's sessions by the id the session list names. A session that
    /// belongs to somebody else answers 404, exactly as an unknown id does, so the route cannot be
    /// used to learn which session ids exist; ending a session already ended succeeds again.</summary>
    /// <remarks>Stopping a session's refresh token does not claw back an access token already issued
    /// in it: like a sign-out, it works from the next refresh on.</remarks>
    [HttpDelete("sessions/{sessionId:guid}")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest, Type = typeof(ValidationProblemDetails))]
    [ProducesResponseType(StatusCodes.Status401Unauthorized, Type = typeof(ProblemDetails))]
    [ProducesResponseType(StatusCodes.Status404NotFound, Type = typeof(ProblemDetails))]
    public async Task<ActionResult> RevokeSession(Guid sessionId, CancellationToken cancellationToken) =>
        NoContentOrProblem(await revokeSession.ExecuteAsync(new RevokeSessionCommand(sessionId), cancellationToken));
}

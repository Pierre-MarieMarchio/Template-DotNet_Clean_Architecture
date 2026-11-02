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
using AppTemplate.Application.Auth.Features.ExternalLogins.UseCases.Commands.SignInWithExternalProvider;
//#if (browserSessions)
using AppTemplate.Application.Auth.Features.Sessions.Model.Errors;
//#endif
using AppTemplate.Application.Auth.Features.Sessions.UseCases.Commands.Login;
using AppTemplate.Application.Auth.Features.Sessions.UseCases.Commands.Logout;
using AppTemplate.Application.Auth.Features.Sessions.UseCases.Commands.LogoutEverywhere;
using AppTemplate.Application.Auth.Features.Sessions.UseCases.Commands.RefreshAccessToken;
using AppTemplate.Application.Auth.Features.Sessions.UseCases.Commands.RevokeSession;
using AppTemplate.Application.Auth.Features.Sessions.UseCases.Queries.ListSessions;
using AppTemplate.Application.Auth.Features.TwoFactor.UseCases.Commands.VerifyTwoFactor;
//#if (browserSessions)
using AppTemplate.Application.Core.Common.Results;
//#endif
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
//#if (browserSessions)
using Microsoft.AspNetCore.Mvc.ModelBinding;
//#endif
using Microsoft.AspNetCore.RateLimiting;

namespace AppTemplate.Api.Features.Auth.Controllers;

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
        //#if (browserSessions)
        [FromHeader(Name = BrowserSession.ModeHeader), RegularExpression(BrowserSession.ModePattern)]
        string? refreshTokenMode,
        //#endif
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var command = new LoginCommand(request.Email, request.Password);
        var response = AuthResponseMapping.ToLoginResponse(await login.ExecuteAsync(command, cancellationToken));
        //#if (browserSessions)

        response = BrowserSession.Deliver(HttpContext, refreshTokenMode, response);
        //#endif

        return OkOrProblem(response);
    }

    /// <summary>
    /// Exchanges the challenge token of a <c>twoFactorRequired</c> login, plus a code, for a token
    /// pair.
    /// </summary>
    [HttpPost("login/two-factor")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitingExtensions.Authentication)]
    [NoStore]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(LoginResponse))]
    [ProducesResponseType(StatusCodes.Status400BadRequest, Type = typeof(ValidationProblemDetails))]
    [ProducesResponseType(StatusCodes.Status401Unauthorized, Type = typeof(ProblemDetails))]
    public async Task<ActionResult<LoginResponse>> LoginWithTwoFactor(
        [FromBody] VerifyTwoFactorRequest request,
        //#if (browserSessions)
        [FromHeader(Name = BrowserSession.ModeHeader), RegularExpression(BrowserSession.ModePattern)]
        string? refreshTokenMode,
        //#endif
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var command = new VerifyTwoFactorCommand(request.ChallengeToken, request.Code);
        var response = AuthResponseMapping.ToLoginResponse(await verifyTwoFactor.ExecuteAsync(command, cancellationToken));
        //#if (browserSessions)

        response = BrowserSession.Deliver(HttpContext, refreshTokenMode, response);
        //#endif

        return OkOrProblem(response);
    }

    /// <summary>
    /// Exchanges an <c>id_token</c> the client obtained from an identity provider for this API's own
    /// token pair, creating or linking a local account on the way.
    /// </summary>
    [HttpPost("login/external")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitingExtensions.Authentication)]
    [NoStore]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(ExternalLoginResponse))]
    [ProducesResponseType(StatusCodes.Status400BadRequest, Type = typeof(ValidationProblemDetails))]
    [ProducesResponseType(StatusCodes.Status401Unauthorized, Type = typeof(ProblemDetails))]
    public async Task<ActionResult<ExternalLoginResponse>> LoginWithExternalProvider(
        [FromBody] SignInWithExternalProviderRequest request,
        //#if (browserSessions)
        [FromHeader(Name = BrowserSession.ModeHeader), RegularExpression(BrowserSession.ModePattern)]
        string? refreshTokenMode,
        //#endif
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var command = new SignInWithExternalProviderCommand(request.Provider, request.IdToken);
        var response = AuthResponseMapping.ToExternalLoginResponse(
            await signInWithExternalProvider.ExecuteAsync(command, cancellationToken));
        //#if (browserSessions)

        response = BrowserSession.Deliver(HttpContext, refreshTokenMode, response);
        //#endif

        return OkOrProblem(response);
    }

    /// <summary>Exchanges a refresh token for a new pair.</summary>
    [HttpPost("refresh")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitingExtensions.Authentication)]
    [NoStore]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(TokenResponse))]
    [ProducesResponseType(StatusCodes.Status400BadRequest, Type = typeof(ValidationProblemDetails))]
    [ProducesResponseType(StatusCodes.Status401Unauthorized, Type = typeof(ProblemDetails))]
    public async Task<ActionResult<TokenResponse>> Refresh(
        /*#if (browserSessions)*/
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] RefreshAccessTokenRequest? request,
        [FromHeader(Name = BrowserSession.ModeHeader), RegularExpression(BrowserSession.ModePattern)]
        string? refreshTokenMode,
        /*#else
        [FromBody] RefreshAccessTokenRequest request,
        #endif*/
        CancellationToken cancellationToken)
    {
        /*#if (browserSessions)*/
        if (BrowserSession.IsRequested(refreshTokenMode))
        {
            return OkOrProblem(await RefreshFromCookieAsync(cancellationToken));
        }

        var command = new RefreshAccessTokenCommand(request?.RefreshToken ?? string.Empty);
        /*#else
        ArgumentNullException.ThrowIfNull(request);

        var command = new RefreshAccessTokenCommand(request.RefreshToken);
        #endif*/

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
        /*#if (browserSessions)*/
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] LogoutRequest? request,
        [FromHeader(Name = BrowserSession.ModeHeader), RegularExpression(BrowserSession.ModePattern)]
        string? refreshTokenMode,
        /*#else
        [FromBody] LogoutRequest request,
        #endif*/
        CancellationToken cancellationToken)
    {
        /*#if (browserSessions)*/
        if (BrowserSession.IsRequested(refreshTokenMode))
        {
            return NoContentOrProblem(await LogoutFromCookieAsync(cancellationToken));
        }

        var command = new LogoutCommand(request?.RefreshToken ?? string.Empty);
        /*#else
        ArgumentNullException.ThrowIfNull(request);

        var command = new LogoutCommand(request.RefreshToken);
        #endif*/

        return NoContentOrProblem(await logout.ExecuteAsync(command, cancellationToken));
    }

    /// <summary>
    /// Revokes every refresh token grant belonging to the caller, ending all of their sessions.
    /// </summary>
    [HttpPost("logout-all")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized, Type = typeof(ProblemDetails))]
    public async Task<ActionResult> LogoutEverywhere(CancellationToken cancellationToken) =>
        NoContentOrProblem(await logoutEverywhere.ExecuteAsync(cancellationToken));

    /// <summary>
    /// Lists the caller's live sessions, most recently used first, marking the one making this
    /// request.
    /// </summary>
    [HttpGet("sessions")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(SessionsResponse))]
    [ProducesResponseType(StatusCodes.Status401Unauthorized, Type = typeof(ProblemDetails))]
    public async Task<ActionResult<SessionsResponse>> ListSessions(CancellationToken cancellationToken) =>
        OkOrProblem(AuthResponseMapping.ToSessionsResponse(await listSessions.ExecuteAsync(cancellationToken)));

    /// <summary>Ends one of the caller's sessions by the id the session list names.</summary>
    [HttpDelete("sessions/{sessionId:guid}")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest, Type = typeof(ValidationProblemDetails))]
    [ProducesResponseType(StatusCodes.Status401Unauthorized, Type = typeof(ProblemDetails))]
    [ProducesResponseType(StatusCodes.Status404NotFound, Type = typeof(ProblemDetails))]
    public async Task<ActionResult> RevokeSession(Guid sessionId, CancellationToken cancellationToken) =>
        NoContentOrProblem(await revokeSession.ExecuteAsync(new RevokeSessionCommand(sessionId), cancellationToken));
    //#if (browserSessions)

    private async Task<Result<TokenResponse>> RefreshFromCookieAsync(CancellationToken cancellationToken)
    {
        if (BrowserSession.Presented(Request) is not { } presented)
        {
            return Result.Failure<TokenResponse>(SessionsErrors.InvalidRefreshToken);
        }

        var command = new RefreshAccessTokenCommand(presented);

        return BrowserSession.Rotate(
            HttpContext,
            AuthResponseMapping.ToTokenResponse(await refreshAccessToken.ExecuteAsync(command, cancellationToken)));
    }

    private async Task<Result> LogoutFromCookieAsync(CancellationToken cancellationToken)
    {
        string? presented = BrowserSession.Presented(Request);

        BrowserSession.Clear(HttpContext);

        return presented is null
            ? Result.Success()
            : await logout.ExecuteAsync(new LogoutCommand(presented), cancellationToken);
    }
    //#endif
}

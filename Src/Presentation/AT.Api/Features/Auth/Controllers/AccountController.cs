using AppTemplate.Api.Common.Contracts;
using AppTemplate.Api.Common.Controllers;
using AppTemplate.Api.Common.Security;
using AppTemplate.Api.Features.Auth.Contracts.Mapping;
using AppTemplate.Api.Features.Auth.Contracts.Requests;
using AppTemplate.Api.Features.Auth.Contracts.Responses;
using AppTemplate.Application.Auth.Features.Accounts.UseCases.Commands.ChangePassword;
using AppTemplate.Application.Auth.Features.Accounts.UseCases.Commands.ConfirmEmailChange;
using AppTemplate.Application.Auth.Features.Accounts.UseCases.Commands.DeleteMyAccount;
using AppTemplate.Application.Auth.Features.Accounts.UseCases.Commands.RequestEmailChange;
using AppTemplate.Application.Auth.Features.SecurityEvents.UseCases.Queries.GetMySecurityEvents;
using AppTemplate.Application.Auth.Features.Sessions.UseCases.Queries.GetCurrentUser;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace AppTemplate.Api.Features.Auth.Controllers;

[Route("api/v{version:apiVersion}/auth")]
[Asp.Versioning.ApiVersion("1.0")]
public sealed class AccountController(
    IGetCurrentUserUseCase getCurrentUser,
    IGetMySecurityEventsUseCase getMySecurityEvents,
    IChangePasswordUseCase changePassword,
    IRequestEmailChangeUseCase requestEmailChange,
    IConfirmEmailChangeUseCase confirmEmailChange,
    IDeleteMyAccountUseCase deleteMyAccount) : ApiControllerBase
{
    /// <summary>The authenticated caller's own profile.</summary>
    [HttpGet("me")]
    [HttpHead("me")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(CurrentUserResponse))]
    [ProducesResponseType(StatusCodes.Status401Unauthorized, Type = typeof(ProblemDetails))]
    public async Task<ActionResult<CurrentUserResponse>> GetCurrentUser(CancellationToken cancellationToken) =>
        OkOrProblem(AuthResponseMapping.ToCurrentUserResponse(await getCurrentUser.ExecuteAsync(cancellationToken)));

    /// <summary>Replaces the caller's password.</summary>
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
    /// Starts a change of the caller's email address by mailing a single-use token to the new one.
    /// </summary>
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

    /// <summary>Confirms a pending email change from the token mailed to the new address.</summary>
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

    /// <summary>Deletes the caller's own account and ends every one of its sessions.</summary>
    [HttpPost("delete-account")]
    [Authorize]
    [EnableRateLimiting(RateLimitingExtensions.Authentication)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest, Type = typeof(ValidationProblemDetails))]
    [ProducesResponseType(StatusCodes.Status401Unauthorized, Type = typeof(ProblemDetails))]
    [ProducesResponseType(StatusCodes.Status403Forbidden, Type = typeof(ProblemDetails))]
    public async Task<ActionResult> DeleteMyAccount(
        [FromBody] DeleteMyAccountRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var command = new DeleteMyAccountCommand(request.CurrentPassword, request.TwoFactorCode);

        return NoContentOrProblem(await deleteMyAccount.ExecuteAsync(command, cancellationToken));
    }

    /// <summary>Lists the caller's own security trail, newest first.</summary>
    [HttpGet("me/security-events")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(PagedResponse<SecurityEventResponse>))]
    [ProducesResponseType(StatusCodes.Status400BadRequest, Type = typeof(ValidationProblemDetails))]
    [ProducesResponseType(StatusCodes.Status401Unauthorized, Type = typeof(ProblemDetails))]
    public async Task<ActionResult<PagedResponse<SecurityEventResponse>>> GetMySecurityEvents(
        [FromQuery] GetSecurityEventsRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var query = new GetMySecurityEventsQuery(request.Page, request.PageSize);

        return OkOrProblem(
            AuthResponseMapping.ToSecurityEventPageResponse(await getMySecurityEvents.ExecuteAsync(query, cancellationToken)));
    }
}

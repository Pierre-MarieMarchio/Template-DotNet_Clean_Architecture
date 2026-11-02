using AppTemplate.Api.Common.Contracts;
using AppTemplate.Api.Common.Controllers;
using AppTemplate.Api.Features.Auth.Contracts.Mapping;
using AppTemplate.Api.Features.Auth.Contracts.Requests;
using AppTemplate.Api.Features.Auth.Contracts.Responses;
using AppTemplate.Application.Auth.Features.Accounts.UseCases.Commands.AddRole;
using AppTemplate.Application.Auth.Features.Accounts.UseCases.Commands.DeleteAccount;
using AppTemplate.Application.Auth.Features.Accounts.UseCases.Commands.LockAccount;
using AppTemplate.Application.Auth.Features.Accounts.UseCases.Commands.RemoveRole;
using AppTemplate.Application.Auth.Features.Accounts.UseCases.Commands.UnlockAccount;
using AppTemplate.Application.Auth.Features.Accounts.UseCases.Queries.ListAccounts;
using AppTemplate.Application.Auth.Features.SecurityEvents.UseCases.Queries.GetAccountSecurityEvents;
using AppTemplate.Application.Auth.Features.TwoFactor.UseCases.Commands.DisableAccountTwoFactor;
using AppTemplate.Presentation.Core.Common.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AppTemplate.Api.Features.Auth.Controllers;

[Route("api/v{version:apiVersion}/auth/accounts")]
[Asp.Versioning.ApiVersion("1.0")]
[Authorize(Policy = AuthorizationPolicies.Administrator)]
[ProducesResponseType(StatusCodes.Status401Unauthorized, Type = typeof(ProblemDetails))]
[ProducesResponseType(StatusCodes.Status403Forbidden, Type = typeof(ProblemDetails))]
public sealed class AccountAdministrationController(
    ILockAccountUseCase lockAccount,
    IUnlockAccountUseCase unlockAccount,
    IAddRoleUseCase addRole,
    IRemoveRoleUseCase removeRole,
    IDeleteAccountUseCase deleteAccount,
    IDisableAccountTwoFactorUseCase disableAccountTwoFactor,
    IGetAccountSecurityEventsUseCase getAccountSecurityEvents,
    IListAccountsUseCase listAccounts) : ApiControllerBase
{
    /// <summary>Lists the accounts by address, optionally those whose address starts with a prefix.</summary>
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(PagedResponse<AccountSummaryResponse>))]
    [ProducesResponseType(StatusCodes.Status400BadRequest, Type = typeof(ValidationProblemDetails))]
    public async Task<ActionResult<PagedResponse<AccountSummaryResponse>>> List(
        [FromQuery] ListAccountsRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var query = new ListAccountsQuery(request.Email, request.Page, request.PageSize);

        return OkOrProblem(AuthResponseMapping.ToAccountPageResponse(await listAccounts.ExecuteAsync(query, cancellationToken)));
    }

    /// <summary>Locks the account out indefinitely.</summary>
    [HttpPost("{userId:guid}/lockout")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest, Type = typeof(ValidationProblemDetails))]
    [ProducesResponseType(StatusCodes.Status404NotFound, Type = typeof(ProblemDetails))]
    public async Task<ActionResult> Lock(Guid userId, CancellationToken cancellationToken) =>
        NoContentOrProblem(await lockAccount.ExecuteAsync(new LockAccountCommand(userId), cancellationToken));

    /// <summary>Lifts an administrative lockout.</summary>
    [HttpDelete("{userId:guid}/lockout")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest, Type = typeof(ValidationProblemDetails))]
    [ProducesResponseType(StatusCodes.Status404NotFound, Type = typeof(ProblemDetails))]
    public async Task<ActionResult> Unlock(Guid userId, CancellationToken cancellationToken) =>
        NoContentOrProblem(await unlockAccount.ExecuteAsync(new UnlockAccountCommand(userId), cancellationToken));

    /// <summary>Grants a role.</summary>
    [HttpPut("{userId:guid}/roles/{role}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest, Type = typeof(ValidationProblemDetails))]
    [ProducesResponseType(StatusCodes.Status404NotFound, Type = typeof(ProblemDetails))]
    public async Task<ActionResult> AddRole(Guid userId, string role, CancellationToken cancellationToken) =>
        NoContentOrProblem(await addRole.ExecuteAsync(new AddRoleCommand(userId, role), cancellationToken));

    /// <summary>Revokes a role.</summary>
    [HttpDelete("{userId:guid}/roles/{role}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest, Type = typeof(ValidationProblemDetails))]
    [ProducesResponseType(StatusCodes.Status404NotFound, Type = typeof(ProblemDetails))]
    public async Task<ActionResult> RemoveRole(Guid userId, string role, CancellationToken cancellationToken) =>
        NoContentOrProblem(await removeRole.ExecuteAsync(new RemoveRoleCommand(userId, role), cancellationToken));

    /// <summary>Deletes the account outright.</summary>
    [HttpDelete("{userId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest, Type = typeof(ValidationProblemDetails))]
    [ProducesResponseType(StatusCodes.Status404NotFound, Type = typeof(ProblemDetails))]
    public async Task<ActionResult> Delete(Guid userId, CancellationToken cancellationToken) =>
        NoContentOrProblem(await deleteAccount.ExecuteAsync(new DeleteAccountCommand(userId), cancellationToken));

    /// <summary>
    /// Strips the account's second factor, for a holder who has lost both the authenticator and the
    /// recovery codes.
    /// </summary>
    [HttpDelete("{userId:guid}/two-factor")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest, Type = typeof(ValidationProblemDetails))]
    [ProducesResponseType(StatusCodes.Status404NotFound, Type = typeof(ProblemDetails))]
    public async Task<ActionResult> DisableTwoFactor(Guid userId, CancellationToken cancellationToken) =>
        NoContentOrProblem(
            await disableAccountTwoFactor.ExecuteAsync(new DisableAccountTwoFactorCommand(userId), cancellationToken));

    /// <summary>Lists the named account's security trail, newest first.</summary>
    [HttpGet("{userId:guid}/security-events")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(PagedResponse<SecurityEventResponse>))]
    [ProducesResponseType(StatusCodes.Status400BadRequest, Type = typeof(ValidationProblemDetails))]
    public async Task<ActionResult<PagedResponse<SecurityEventResponse>>> GetSecurityEvents(
        Guid userId,
        [FromQuery] GetSecurityEventsRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var query = new GetAccountSecurityEventsQuery(userId, request.Page, request.PageSize);

        return OkOrProblem(
            AuthResponseMapping.ToSecurityEventPageResponse(await getAccountSecurityEvents.ExecuteAsync(query, cancellationToken)));
    }
}

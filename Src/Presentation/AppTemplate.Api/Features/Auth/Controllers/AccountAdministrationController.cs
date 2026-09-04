using AppTemplate.Api.Common.Controllers;
using AppTemplate.Api.Common.Security;
using AppTemplate.Application.Features.Auth.UseCases.Commands.AddRole;
using AppTemplate.Application.Features.Auth.UseCases.Commands.DeleteAccount;
using AppTemplate.Application.Features.Auth.UseCases.Commands.DisableAccountTwoFactor;
using AppTemplate.Application.Features.Auth.UseCases.Commands.LockAccount;
using AppTemplate.Application.Features.Auth.UseCases.Commands.RemoveRole;
using AppTemplate.Application.Features.Auth.UseCases.Commands.UnlockAccount;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AppTemplate.Api.Features.Auth.Controllers;

/// <summary>
/// Acting on somebody else's account: locking it out, granting or revoking a role, deleting it
/// outright. Everything here is restricted to <see cref="AuthorizationPolicies.Administrator"/>, on the class —
/// unlike <see cref="AuthController"/>, nothing on this surface has an anonymous or self-service
/// counterpart, so there is no accidental <c>[AllowAnonymous]</c> for a class-level policy to defeat.
/// </summary>
/// <remarks>
/// No <see cref="RateLimitingExtensions.Authentication"/> budget: nothing here handles a credential,
/// so none of it belongs on the allowance that exists to slow brute-force login guessing down. An
/// administrator's own request is already behind a valid access token and the Administrator policy.
/// </remarks>
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
    IDisableAccountTwoFactorUseCase disableAccountTwoFactor) : ApiControllerBase
{
    /// <summary>
    /// Locks the account out indefinitely. Rotates its security stamp, so an access token it already
    /// holds stops working on its very next request rather than staying valid until it expires.
    /// </summary>
    [HttpPost("{userId:guid}/lockout")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound, Type = typeof(ProblemDetails))]
    public async Task<ActionResult> Lock(Guid userId, CancellationToken cancellationToken) =>
        NoContentOrProblem(await lockAccount.ExecuteAsync(new LockAccountCommand(userId), cancellationToken));

    /// <summary>Lifts an administrative lockout. A no-op, not a 404, on an account that was not locked.</summary>
    [HttpDelete("{userId:guid}/lockout")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound, Type = typeof(ProblemDetails))]
    public async Task<ActionResult> Unlock(Guid userId, CancellationToken cancellationToken) =>
        NoContentOrProblem(await unlockAccount.ExecuteAsync(new UnlockAccountCommand(userId), cancellationToken));

    /// <summary>
    /// Grants a role. <c>PUT</c>, not <c>POST</c>: asking twice for the same account to carry the
    /// same role is idempotent in effect even where the store answers the second call with a refusal
    /// naming it already assigned.
    /// </summary>
    [HttpPut("{userId:guid}/roles/{role}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest, Type = typeof(ValidationProblemDetails))]
    [ProducesResponseType(StatusCodes.Status404NotFound, Type = typeof(ProblemDetails))]
    public async Task<ActionResult> AddRole(Guid userId, string role, CancellationToken cancellationToken) =>
        NoContentOrProblem(await addRole.ExecuteAsync(new AddRoleCommand(userId, role), cancellationToken));

    /// <summary>
    /// Revokes a role. Refused with a 403 when <paramref name="userId"/> names the caller: see
    /// <c>RemoveRoleUseCase</c> for why the guard is not narrower than "any role, from yourself".
    /// </summary>
    [HttpDelete("{userId:guid}/roles/{role}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest, Type = typeof(ValidationProblemDetails))]
    [ProducesResponseType(StatusCodes.Status404NotFound, Type = typeof(ProblemDetails))]
    public async Task<ActionResult> RemoveRole(Guid userId, string role, CancellationToken cancellationToken) =>
        NoContentOrProblem(await removeRole.ExecuteAsync(new RemoveRoleCommand(userId, role), cancellationToken));

    /// <summary>
    /// Deletes the account outright. Refused with a 403 when <paramref name="userId"/> names the
    /// caller — see <c>DeleteAccountUseCase</c>. Deleting a to-do list or a reminder this account
    /// owns is not part of this call: see <c>IAccountDeletion</c> for why, and for what becomes of
    /// them.
    /// </summary>
    [HttpDelete("{userId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound, Type = typeof(ProblemDetails))]
    public async Task<ActionResult> Delete(Guid userId, CancellationToken cancellationToken) =>
        NoContentOrProblem(await deleteAccount.ExecuteAsync(new DeleteAccountCommand(userId), cancellationToken));

    /// <summary>
    /// Strips the account's second factor without its password or a code — the escape hatch for
    /// somebody who has lost the authenticator app <em>and</em> the recovery codes, and so can prove
    /// neither. Without this, the only recourse was <see cref="Delete"/>, which throws the account
    /// away along with everything it owns rather than the one thing actually stuck.
    /// <para>
    /// Refused with a 403 when <paramref name="userId"/> names the caller — not <see cref="Lock"/>'s
    /// reason, since disabling two-factor sign-in on the caller's own account locks nothing and
    /// leaves it fully reachable afterward. It is refused because letting this route reach the
    /// caller's own account would let a stolen administrator session strip that account's second
    /// factor without ever presenting the password the self-service route demands — see
    /// <c>DisableAccountTwoFactorUseCase</c>.
    /// </para>
    /// </summary>
    [HttpDelete("{userId:guid}/two-factor")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest, Type = typeof(ValidationProblemDetails))]
    [ProducesResponseType(StatusCodes.Status404NotFound, Type = typeof(ProblemDetails))]
    public async Task<ActionResult> DisableTwoFactor(Guid userId, CancellationToken cancellationToken) =>
        NoContentOrProblem(
            await disableAccountTwoFactor.ExecuteAsync(new DisableAccountTwoFactorCommand(userId), cancellationToken));
}

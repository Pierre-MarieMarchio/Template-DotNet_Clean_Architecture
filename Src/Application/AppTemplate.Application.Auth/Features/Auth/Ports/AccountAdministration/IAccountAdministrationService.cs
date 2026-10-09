namespace AppTemplate.Application.Auth.Features.Auth.Ports.AccountAdministration;

/// <summary>
/// What an administrator does to someone else's account, on the administrator's own authority rather
/// than that account's credential: suspend and release it, grant and revoke roles, strip a stuck
/// second factor, and delete it.
/// <para>
/// Every operation that takes access away — lock, grant, revoke, two-factor removal — rotates the
/// account's security stamp, so access tokens already issued stop validating. Refresh tokens are
/// untouched by that rotation and are the caller's to revoke. Unlocking takes nothing away and
/// rotates nothing.
/// </para>
/// </summary>
public interface IAccountAdministrationService
{
    /// <summary>
    /// Locks the account with no expiry: an administrative lock is lifted by <see cref="UnlockAsync"/>,
    /// not by a clock.
    /// </summary>
    Task<AccountAdministrationOutcome> LockAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lifts any lockout, administrative or automatic. A no-op, not a failure, on an account that is
    /// not locked out.
    /// </summary>
    Task<AccountAdministrationOutcome> UnlockAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>Grants a role that already exists; an unknown role is a rejection naming it.</summary>
    Task<AccountAdministrationOutcome> GrantRoleAsync(
        Guid userId,
        string role,
        CancellationToken cancellationToken = default);

    /// <summary>Revokes a role the account holds.</summary>
    Task<AccountAdministrationOutcome> RevokeRoleAsync(
        Guid userId,
        string role,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Turns two-factor sign-in off unconditionally — no password and no code, because the caller
    /// proved who they are through the <c>Administrator</c> policy. The recourse for a holder who lost
    /// both the authenticator app and the recovery codes. A no-op, not a failure, on an account that
    /// already has it off.
    /// </summary>
    Task<AccountAdministrationOutcome> DisableTwoFactorAsync(
        Guid userId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes the account and what the user store holds for it, its refresh-token grants included.
    /// Rows another feature owns under this id are not touched: this module names no other feature,
    /// so those rows stay owned by an id that no longer resolves, visible to no one, since every
    /// feature scopes its queries by the caller's own id.
    /// </summary>
    Task<AccountAdministrationOutcome> DeleteAsync(Guid userId, CancellationToken cancellationToken = default);
}

using AppTemplate.Application.Auth.Features.Auth.Ports.AccountAdministration;
using AppTemplate.Infrastructure.Auth.Common.Directories;
using AppTemplate.Infrastructure.Auth.Features.Auth.Models;
using Microsoft.AspNetCore.Identity;

namespace AppTemplate.Infrastructure.Auth.Features.Auth.Services;

/// <summary>
/// <see cref="IAccountAdministrationService"/> over <see cref="UserManager{TUser}"/>. Role names are
/// passed through unexamined — this module knows no role names of its own.
/// <para>
/// <see cref="RoleManager{TRole}"/> is here only to guard <see cref="GrantRoleAsync"/> against a role
/// that was never seeded. <c>UserManager.AddToRoleAsync</c> checks whether the user already carries
/// the role, but not whether the role exists — the Entity Framework store throws
/// <see cref="InvalidOperationException"/> for that case instead of returning a refusal.
/// <c>RemoveFromRoleAsync</c> needs no such guard: removing a role nobody has is already a normal
/// refusal.
/// </para>
/// <para>
/// Deleting needs no stamp rotation and no grant revocation: the bearer handler looks the account up
/// by id before it compares a stamp, and <c>RefreshTokens</c> carries a cascading foreign key to the
/// account, so removing the row removes every grant with it.
/// </para>
/// </summary>
internal sealed class AccountAdministrationService(
    UserManager<AppUser> userManager,
    RoleManager<AppRole> roleManager,
    IAppUserDirectory directory) : IAccountAdministrationService
{
    public async Task<AccountAdministrationOutcome> LockAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var user = await directory.FindByIdAsync(userId, cancellationToken);

        if (user is null)
        {
            return AccountAdministrationOutcome.NoSuchAccount;
        }

        // A lockout end date does nothing on an account whose LockoutEnabled flag is false, and a
        // store seeded outside CreateAsync may not carry it.
        var enabled = await userManager.SetLockoutEnabledAsync(user, true);

        if (!enabled.Succeeded)
        {
            return AccountAdministrationOutcome.Rejected;
        }

        // No expiry: an administrative lock is lifted by UnlockAsync, not by a clock.
        var locked = await userManager.SetLockoutEndDateAsync(user, DateTimeOffset.MaxValue);

        if (!locked.Succeeded)
        {
            return AccountAdministrationOutcome.Rejected;
        }

        // Identity does not treat a lockout as a credential change, so without this the token
        // already in the locked-out caller's hands keeps validating until it expires.
        await userManager.UpdateSecurityStampAsync(user);

        return AccountAdministrationOutcome.Applied;
    }

    public async Task<AccountAdministrationOutcome> UnlockAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var user = await directory.FindByIdAsync(userId, cancellationToken);

        if (user is null)
        {
            return AccountAdministrationOutcome.NoSuchAccount;
        }

        // No stamp rotation: lifting a lockout takes no access away, so there is nothing to
        // invalidate.
        var unlocked = await userManager.SetLockoutEndDateAsync(user, null);

        if (unlocked.Succeeded)
        {
            return AccountAdministrationOutcome.Applied;
        }

        // SetLockoutEndDateAsync refuses when LockoutEnabled is false, which is also the state of an
        // account nobody has ever locked out: "not locked out" either way, so it is a no-op.
        bool wasNeverLockable = unlocked.Errors.Any(error =>
            string.Equals(error.Code, "UserLockoutNotEnabled", StringComparison.Ordinal));

        return wasNeverLockable ? AccountAdministrationOutcome.Applied : AccountAdministrationOutcome.Rejected;
    }

    public async Task<AccountAdministrationOutcome> GrantRoleAsync(
        Guid userId,
        string role,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(role);
        cancellationToken.ThrowIfCancellationRequested();

        var user = await directory.FindByIdAsync(userId, cancellationToken);

        if (user is null)
        {
            return AccountAdministrationOutcome.NoSuchAccount;
        }

        if (!await roleManager.RoleExistsAsync(role))
        {
            return AccountAdministrationOutcome.RejectedBecause($"Role '{role}' does not exist.");
        }

        var result = await userManager.AddToRoleAsync(user, role);

        if (!result.Succeeded)
        {
            return AccountAdministrationOutcome.RejectedBecause(Describe(result));
        }

        // AddToRoleAsync does not rotate the security stamp on its own. Without this, a role granted
        // just now has no effect on a caller's access until their current access token expires.
        await userManager.UpdateSecurityStampAsync(user);

        return AccountAdministrationOutcome.Applied;
    }

    public async Task<AccountAdministrationOutcome> RevokeRoleAsync(
        Guid userId,
        string role,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(role);
        cancellationToken.ThrowIfCancellationRequested();

        var user = await directory.FindByIdAsync(userId, cancellationToken);

        if (user is null)
        {
            return AccountAdministrationOutcome.NoSuchAccount;
        }

        var result = await userManager.RemoveFromRoleAsync(user, role);

        if (!result.Succeeded)
        {
            return AccountAdministrationOutcome.RejectedBecause(Describe(result));
        }

        // Same gap as GrantRoleAsync, in the other direction: without this, a role just revoked keeps
        // authorising whatever it granted for as long as the caller's current access token is valid.
        await userManager.UpdateSecurityStampAsync(user);

        return AccountAdministrationOutcome.Applied;
    }

    public async Task<AccountAdministrationOutcome> DisableTwoFactorAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var user = await directory.FindByIdAsync(userId, cancellationToken);

        if (user is null)
        {
            return AccountAdministrationOutcome.NoSuchAccount;
        }

        // No password to check: the administrator's authority stands in for the account's own proof.
        return await TwoFactorService.TurnOffAsync(userManager, user)
            ? AccountAdministrationOutcome.Applied
            : AccountAdministrationOutcome.Rejected;
    }

    public async Task<AccountAdministrationOutcome> DeleteAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var user = await directory.FindByIdAsync(userId, cancellationToken);

        if (user is null)
        {
            return AccountAdministrationOutcome.NoSuchAccount;
        }

        var result = await userManager.DeleteAsync(user);

        return result.Succeeded ? AccountAdministrationOutcome.Applied : AccountAdministrationOutcome.Rejected;
    }

    private static string Describe(IdentityResult result) =>
        string.Join(" ", result.Errors.Select(error => error.Description));
}

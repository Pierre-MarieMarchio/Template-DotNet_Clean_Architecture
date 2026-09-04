using AppTemplate.Application.Features.Auth.Ports.AccountLockouts;
using AppTemplate.Infrastructure.Persistence.Features.Identity.Models;
using Microsoft.AspNetCore.Identity;

namespace AppTemplate.Infrastructure.Identity.Users;

/// <summary>
/// <see cref="IAccountLockouts"/> over <see cref="UserManager{TUser}"/>.
/// </summary>
internal sealed class AccountLockouts(
    UserManager<AppUser> userManager,
    IAppUserDirectory directory) : IAccountLockouts
{
    public async Task<LockoutChangeStatus> LockAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var user = await directory.FindByIdAsync(userId, cancellationToken);

        if (user is null)
        {
            return LockoutChangeStatus.NoSuchAccount;
        }

        // A lockout end date has no effect on an account whose LockoutEnabled flag is false, and this
        // adapter cannot assume it is already set: every account UserAccounts.CreateAsync creates gets
        // it, through IdentityOptions.Lockout.AllowedForNewUsers, but a store seeded another way might
        // not carry it. Setting the flag explicitly is what makes an administrative lock actually take
        // effect rather than silently do nothing.
        var enabled = await userManager.SetLockoutEnabledAsync(user, true);

        if (!enabled.Succeeded)
        {
            return LockoutChangeStatus.Rejected;
        }

        // No expiry: an administrative lock is lifted by UnlockAsync, not by a clock, which is what
        // distinguishes it from the automatic threshold VerifyCredentialAsync enforces.
        var locked = await userManager.SetLockoutEndDateAsync(user, DateTimeOffset.MaxValue);

        if (!locked.Succeeded)
        {
            return LockoutChangeStatus.Rejected;
        }

        // SetLockoutEndDateAsync does not rotate the security stamp on its own — unlike
        // ChangePasswordAsync, Identity does not treat a lockout as a credential change. Without this,
        // the access token already in the now-locked-out caller's hands keeps validating until it
        // expires on its own, which is exactly the gap an administrative lockout exists to close.
        await userManager.UpdateSecurityStampAsync(user);

        return LockoutChangeStatus.Applied;
    }

    public async Task<LockoutChangeStatus> UnlockAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var user = await directory.FindByIdAsync(userId, cancellationToken);

        if (user is null)
        {
            return LockoutChangeStatus.NoSuchAccount;
        }

        // No stamp rotation here: lifting a lockout grants access back rather than taking it away, so
        // there is no live credential this needs to invalidate.
        var unlocked = await userManager.SetLockoutEndDateAsync(user, null);

        if (unlocked.Succeeded)
        {
            return LockoutChangeStatus.Applied;
        }

        // SetLockoutEndDateAsync refuses outright when the account's LockoutEnabled flag is false —
        // which is also exactly the state an account is in when nobody has ever locked it out. That
        // is "not locked out" either way, so it is the no-op this method already promises, not a
        // real refusal.
        bool wasNeverLockable = unlocked.Errors.Any(error =>
            string.Equals(error.Code, "UserLockoutNotEnabled", StringComparison.Ordinal));

        return wasNeverLockable ? LockoutChangeStatus.Applied : LockoutChangeStatus.Rejected;
    }
}

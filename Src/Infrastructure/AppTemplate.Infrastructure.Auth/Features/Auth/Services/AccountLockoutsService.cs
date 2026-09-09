using AppTemplate.Application.Auth.Features.Auth.Ports.AccountLockouts;
using AppTemplate.Infrastructure.Auth.Common.Directories;
using AppTemplate.Infrastructure.Auth.Features.Auth.Models;
using Microsoft.AspNetCore.Identity;

namespace AppTemplate.Infrastructure.Auth.Features.Auth.Services;

/// <summary>
/// <see cref="IAccountLockoutsService"/> over <see cref="UserManager{TUser}"/>.
/// </summary>
internal sealed class AccountLockoutsService(
    UserManager<AppUser> userManager,
    IAppUserDirectory directory) : IAccountLockoutsService
{
    public async Task<LockoutChangeStatus> LockAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var user = await directory.FindByIdAsync(userId, cancellationToken);

        if (user is null)
        {
            return LockoutChangeStatus.NoSuchAccount;
        }

        // A lockout end date does nothing on an account whose LockoutEnabled flag is false, and a
        // store seeded outside CreateAsync may not carry it.
        var enabled = await userManager.SetLockoutEnabledAsync(user, true);

        if (!enabled.Succeeded)
        {
            return LockoutChangeStatus.Rejected;
        }

        // No expiry: an administrative lock is lifted by UnlockAsync, not by a clock.
        var locked = await userManager.SetLockoutEndDateAsync(user, DateTimeOffset.MaxValue);

        if (!locked.Succeeded)
        {
            return LockoutChangeStatus.Rejected;
        }

        // Identity does not treat a lockout as a credential change, so without this the token
        // already in the locked-out caller's hands keeps validating until it expires.
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

        // No stamp rotation: lifting a lockout takes no access away, so there is nothing to
        // invalidate.
        var unlocked = await userManager.SetLockoutEndDateAsync(user, null);

        if (unlocked.Succeeded)
        {
            return LockoutChangeStatus.Applied;
        }

        // SetLockoutEndDateAsync refuses when LockoutEnabled is false, which is also the state of an
        // account nobody has ever locked out: "not locked out" either way, so it is a no-op.
        bool wasNeverLockable = unlocked.Errors.Any(error =>
            string.Equals(error.Code, "UserLockoutNotEnabled", StringComparison.Ordinal));

        return wasNeverLockable ? LockoutChangeStatus.Applied : LockoutChangeStatus.Rejected;
    }
}

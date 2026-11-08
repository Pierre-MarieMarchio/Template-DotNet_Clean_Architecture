using AppTemplate.Application.Auth.Features.Accounts.Ports.AccountAdministration;
using AppTemplate.Infrastructure.Auth.Features.Accounts.Adapters.Directories;
using AppTemplate.Infrastructure.Auth.Features.Accounts.Rows.Models;
using AppTemplate.Infrastructure.Auth.Features.TwoFactor.Adapters.Services;
using Microsoft.AspNetCore.Identity;

namespace AppTemplate.Infrastructure.Auth.Features.Accounts.Adapters.Services;

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

        var enabled = await userManager.SetLockoutEnabledAsync(user, true);

        if (!enabled.Succeeded)
        {
            return AccountAdministrationOutcome.Rejected;
        }

        var locked = await userManager.SetLockoutEndDateAsync(user, DateTimeOffset.MaxValue);

        if (!locked.Succeeded)
        {
            return AccountAdministrationOutcome.Rejected;
        }

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

        var unlocked = await userManager.SetLockoutEndDateAsync(user, null);

        if (unlocked.Succeeded)
        {
            return AccountAdministrationOutcome.Applied;
        }

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

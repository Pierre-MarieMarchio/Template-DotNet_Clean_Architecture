using AppTemplate.Application.Features.Auth.Ports.TwoFactorAdministration;
using AppTemplate.Infrastructure.Persistence.Features.Identity.Models;
using Microsoft.AspNetCore.Identity;

namespace AppTemplate.Infrastructure.Identity.Users;

/// <summary>
/// <see cref="ITwoFactorAdministration"/> over <see cref="UserManager{TUser}"/>.
/// </summary>
internal sealed class TwoFactorAdministration(
    UserManager<AppUser> userManager,
    IAppUserDirectory directory) : ITwoFactorAdministration
{
    public async Task<TwoFactorAdministrativeDisableStatus> DisableAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var user = await directory.FindByIdAsync(userId, cancellationToken);

        if (user is null)
        {
            return TwoFactorAdministrativeDisableStatus.NoSuchAccount;
        }

        // No password to check here — see ITwoFactorAdministration. Rotates the security stamp as a
        // side effect, unconditionally, whether the account had its second factor armed or not — see
        // DisableAccountTwoFactorUseCase for what that invalidates.
        var disabled = await userManager.SetTwoFactorEnabledAsync(user, false);

        if (!disabled.Succeeded)
        {
            return TwoFactorAdministrativeDisableStatus.Rejected;
        }

        // Invalidates the secret too, so a later re-enrollment starts from a fresh one instead of the
        // same key every authenticator app already on file for this account still knows — see
        // TwoFactorEnrollment.DisableAsync for the same reasoning.
        await userManager.ResetAuthenticatorKeyAsync(user);

        return TwoFactorAdministrativeDisableStatus.Disabled;
    }
}

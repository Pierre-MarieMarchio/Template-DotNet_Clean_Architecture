using AppTemplate.Application.Features.Auth.Ports.TwoFactorAdministration;
using AppTemplate.Infrastructure.Identity.Common.Directories;
using AppTemplate.Infrastructure.Persistence.Features.Identity.Models;
using Microsoft.AspNetCore.Identity;

namespace AppTemplate.Infrastructure.Identity.Features.Auth.Services;

/// <summary>
/// <see cref="ITwoFactorAdministrationService"/> over <see cref="UserManager{TUser}"/>.
/// </summary>
internal sealed class TwoFactorAdministrationService(
    UserManager<AppUser> userManager,
    IAppUserDirectory directory) : ITwoFactorAdministrationService
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

        // No password to check here — see ITwoFactorAdministrationService. Rotates the security stamp as a
        // side effect, unconditionally, whether the account had its second factor armed or not — see
        // DisableAccountTwoFactorUseCase for what that invalidates.
        var disabled = await userManager.SetTwoFactorEnabledAsync(user, false);

        if (!disabled.Succeeded)
        {
            return TwoFactorAdministrativeDisableStatus.Rejected;
        }

        // Invalidates the secret too, so a later re-enrollment starts from a fresh one instead of the
        // same key every authenticator app already on file for this account still knows — see
        // TwoFactorEnrollmentService.DisableAsync for the same reasoning.
        await userManager.ResetAuthenticatorKeyAsync(user);

        return TwoFactorAdministrativeDisableStatus.Disabled;
    }
}

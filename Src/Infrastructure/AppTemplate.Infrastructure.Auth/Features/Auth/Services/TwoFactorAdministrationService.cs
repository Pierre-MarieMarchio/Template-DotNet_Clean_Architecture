using AppTemplate.Application.Auth.Features.Auth.Ports.TwoFactorAdministration;
using AppTemplate.Infrastructure.Auth.Common.Directories;
using AppTemplate.Infrastructure.Auth.Features.Auth.Models;
using Microsoft.AspNetCore.Identity;

namespace AppTemplate.Infrastructure.Auth.Features.Auth.Services;

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

        // No password to check here — see ITwoFactorAdministrationService. See
        // DisableAccountTwoFactorUseCase for what the stamp rotation invalidates.
        return await TwoFactorEnrollmentService.TurnOffAsync(userManager, user)
            ? TwoFactorAdministrativeDisableStatus.Disabled
            : TwoFactorAdministrativeDisableStatus.Rejected;
    }
}

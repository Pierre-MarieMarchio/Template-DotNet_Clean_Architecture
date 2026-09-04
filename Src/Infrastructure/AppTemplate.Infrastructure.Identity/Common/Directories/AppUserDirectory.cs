using System.Globalization;
using System.Security.Claims;
using AppTemplate.Infrastructure.Persistence.Features.Identity.Models;
using Microsoft.AspNetCore.Identity;

namespace AppTemplate.Infrastructure.Identity.Common.Directories;

internal sealed class AppUserDirectory(
    UserManager<AppUser> userManager,
    SignInManager<AppUser> signInManager) : IAppUserDirectory
{
    public async Task<AppUser?> FindByIdAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // "D" rather than the default, so the format is stated: UserManager parses the string with
        // the key type's own converter, and a Guid round-trips through both forms.
        return await userManager.FindByIdAsync(userId.ToString("D", CultureInfo.InvariantCulture));
    }

    public async Task<IReadOnlyCollection<Claim>> GenerateClaimsAsync(
        AppUser user,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(user);
        cancellationToken.ThrowIfCancellationRequested();

        var principal = await signInManager.ClaimsFactory.CreateAsync(user);

        return [.. principal.Claims];
    }
}

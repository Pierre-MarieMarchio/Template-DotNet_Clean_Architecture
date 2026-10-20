using System.Globalization;
using System.Security.Claims;
using AppTemplate.Infrastructure.Auth.Features.Accounts.Rows.Models;
using Microsoft.AspNetCore.Identity;

namespace AppTemplate.Infrastructure.Auth.Features.Accounts.Adapters.Directories;

internal sealed class AppUserDirectory(
    UserManager<AppUser> userManager,
    SignInManager<AppUser> signInManager) : IAppUserDirectory
{
    public async Task<AppUser?> FindByIdAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

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

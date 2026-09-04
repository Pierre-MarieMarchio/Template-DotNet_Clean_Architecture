using System.Globalization;
using AppTemplate.Application.Features.Auth.Ports.UserProfiles;
using AppTemplate.Infrastructure.Persistence.Features.Identity.Models;
using Microsoft.AspNetCore.Identity;

namespace AppTemplate.Infrastructure.Identity.Accounts;

/// <summary>
/// <see cref="IUserProfilesService"/> over <see cref="UserManager{TUser}"/>. Every field is read from the
/// store at call time — see <see cref="IUserProfilesService"/> for why a claims-based shortcut is refused.
/// </summary>
internal sealed class UserProfilesService(UserManager<AppUser> userManager) : IUserProfilesService
{
    public async Task<UserProfile?> FindByIdAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var user = await userManager.FindByIdAsync(userId.ToString("D", CultureInfo.InvariantCulture));

        if (user is null)
        {
            return null;
        }

        var roles = await userManager.GetRolesAsync(user);

        return new UserProfile(
            user.Id,
            user.UserName ?? string.Empty,
            user.Email ?? string.Empty,
            user.EmailConfirmed,
            [.. roles],
            user.CreatedAt,
            user.TwoFactorEnabled);
    }
}

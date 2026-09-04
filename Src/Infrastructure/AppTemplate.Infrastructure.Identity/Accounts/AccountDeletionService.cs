using AppTemplate.Application.Features.Auth.Ports.AccountDeletion;
using AppTemplate.Infrastructure.Persistence.Features.Identity.Models;
using Microsoft.AspNetCore.Identity;

namespace AppTemplate.Infrastructure.Identity.Users;

/// <summary>
/// <see cref="IAccountDeletion"/> over <see cref="UserManager{TUser}"/>.
/// <para>
/// Deleting the row is enough on its own, unlike a lockout or a role change. There is no security
/// stamp to rotate: the bearer handler's <c>ValidateSecurityStampAsync</c> looks the account up by
/// id before it ever compares a stamp, and an id that no longer resolves fails there regardless. There
/// is no refresh-token grant to revoke either — <c>RefreshTokens</c> carries a foreign key to the
/// account with cascading delete, so removing the row removes every grant with it at the database
/// level. See <see cref="IAccountDeletion"/> for what this deliberately does <em>not</em> reach for.
/// </para>
/// </summary>
internal sealed class AccountDeletion(
    UserManager<AppUser> userManager,
    IAppUserDirectory directory) : IAccountDeletion
{
    public async Task<AccountDeletionStatus> DeleteAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var user = await directory.FindByIdAsync(userId, cancellationToken);

        if (user is null)
        {
            return AccountDeletionStatus.NoSuchAccount;
        }

        var result = await userManager.DeleteAsync(user);

        return result.Succeeded ? AccountDeletionStatus.Deleted : AccountDeletionStatus.Rejected;
    }
}

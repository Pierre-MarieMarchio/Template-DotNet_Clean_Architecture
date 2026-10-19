using AppTemplate.Application.Auth.Features.Accounts.Ports.Accounts;
using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Domain.Core.Common.Primitives;

namespace AppTemplate.Api.Features.Auth.Directories;

/// <summary>
/// <see cref="IUserAccountDirectory"/> over the local accounts: an id names an account exactly
/// when the user store still holds a profile for it. Neither the directory's contract nor the
/// accounts' feature names the other, so this adapter is where they meet.
/// </summary>
internal sealed class AccountUserAccountDirectory(IAccountsService accounts) : IUserAccountDirectory
{
    public async Task<bool> ExistsAsync(UserId userId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(userId);

        return await accounts.FindProfileAsync(userId.Value, cancellationToken) is not null;
    }
}

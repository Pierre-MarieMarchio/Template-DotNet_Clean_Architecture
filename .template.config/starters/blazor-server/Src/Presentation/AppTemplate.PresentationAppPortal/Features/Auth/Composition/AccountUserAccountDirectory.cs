using AppTemplate.Application.Auth.Features.Accounts.Ports.Accounts;
using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Domain.Core.Common.Primitives;

namespace AppTemplate.PresentationAppPortal.Features.Auth.Composition;

internal sealed class AccountUserAccountDirectory(IAccountsService accounts) : IUserAccountDirectory
{
    public async Task<bool> ExistsAsync(UserId userId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(userId);

        return await accounts.FindProfileAsync(userId.Value, cancellationToken) is not null;
    }
}

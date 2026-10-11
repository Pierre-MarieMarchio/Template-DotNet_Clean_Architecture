using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Application.Features.ExternalIdentity.Services;
using AppTemplate.Domain.Core.Common.Primitives;

namespace AppTemplate.Api.Features.ExternalIdentity.Directories;

/// <summary>
/// <see cref="IUserAccountDirectory"/> over the external-identity mapping: an id names an account
/// exactly when some provider's subject is still mapped to it. Neither the directory's contract
/// nor the mapping's feature names the other, so this adapter is where they meet.
/// </summary>
internal sealed class ExternalIdentityUserAccountDirectory(
    IExternalIdentityService externalIdentities) : IUserAccountDirectory
{
    public async Task<bool> ExistsAsync(UserId userId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(userId);

        return await externalIdentities.FindProfileAsync(userId, cancellationToken) is not null;
    }
}

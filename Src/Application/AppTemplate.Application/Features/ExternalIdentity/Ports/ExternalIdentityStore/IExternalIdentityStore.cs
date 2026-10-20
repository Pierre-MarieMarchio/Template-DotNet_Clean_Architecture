using AppTemplate.Domain.Core.Common.Primitives;

namespace AppTemplate.Application.Features.ExternalIdentity.Ports.ExternalIdentityStore;

public interface IExternalIdentityStore
{
    Task<ExternalIdentityLink?> FindAsync(
        string issuer,
        string subject,
        CancellationToken cancellationToken = default);

    Task<bool> TryAddAsync(ExternalIdentityLink link, CancellationToken cancellationToken = default);

    Task RefreshAsync(ExternalIdentityLink link, CancellationToken cancellationToken = default);

    Task<int> RemoveAsync(UserId userId, CancellationToken cancellationToken = default);
}

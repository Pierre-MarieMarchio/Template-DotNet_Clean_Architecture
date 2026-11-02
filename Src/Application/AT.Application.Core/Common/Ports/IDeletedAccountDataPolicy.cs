using AppTemplate.Domain.Core.Common.Primitives;

namespace AppTemplate.Application.Core.Common.Ports;

public interface IDeletedAccountDataPolicy
{
    Task<bool> ApplyAsync(UserId owner, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<UserId>> ListDataOwnerIdsAsync(CancellationToken cancellationToken = default);
}

using AppTemplate.Domain.Core.Common.Primitives;
using AppTemplate.Domain.Features.Files.Model.Entities;

namespace AppTemplate.Domain.Features.Files.Repositories;

public interface IStoredFileRepository
{
    Task<StoredFile?> GetAsync(Guid id, CancellationToken cancellationToken);

    Task<IReadOnlyList<StoredFile>> GetPendingRegisteredBeforeAsync(
        DateTimeOffset registeredBefore,
        int batchSize,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<StoredFile>> GetDepositedAsync(
        DateTimeOffset asOf,
        int batchSize,
        CancellationToken cancellationToken);

    void Add(StoredFile storedFile);

    Task<IReadOnlyList<StoredFile>> GetForOwnerAsync(
        UserId ownerId,
        CancellationToken cancellationToken);

    void Remove(StoredFile storedFile);
}

using AppTemplate.Domain.Core.Common.Primitives;
using AppTemplate.Domain.Features.Files.Entities;

namespace AppTemplate.Domain.Features.Files.Repositories;

/// <summary>Aggregate access for <see cref="StoredFile"/>: one method per thing a use case needs,
/// in domain types only. Listing files and the set of live object keys are projections, not
/// aggregates, and belong to <c>Queries</c> (see <c>docs/DECISIONS.md</c> on the four storage
/// words). Nothing here writes: <c>Add</c> and <c>Remove</c> stage, <c>IUnitOfWork</c> commits.</summary>
public interface IStoredFileRepository
{
    /// <returns>The file, or <c>null</c> when no file has that id.</returns>
    Task<StoredFile?> GetAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// Registrations never deposited against, oldest first, capped at <paramref name="batchSize"/>;
    /// a coarse filter — <see cref="StoredFile.IsAbandoned"/> decides whether one may be given up on.
    /// </summary>
    /// <param name="registeredBefore">Only files registered strictly before this instant.</param>
    Task<IReadOnlyList<StoredFile>> GetPendingRegisteredBeforeAsync(
        DateTimeOffset registeredBefore,
        int batchSize,
        CancellationToken cancellationToken);

    /// <summary>Deposited files due for inspection at <paramref name="asOf"/>, ordered by when
    /// they became due (<see cref="StoredFile.NextInspectionAt"/>, else
    /// <see cref="StoredFile.RegisteredAt"/>, then id), capped at <paramref name="batchSize"/>;
    /// a deferred file queues behind those that waited longer.</summary>
    Task<IReadOnlyList<StoredFile>> GetDepositedAsync(
        DateTimeOffset asOf,
        int batchSize,
        CancellationToken cancellationToken);

    void Add(StoredFile storedFile);

    /// <summary>
    /// Every file one owner holds, with tags, as aggregates: the account-deleted policy removes
    /// through the aggregate — <c>StoredFile.Delete</c> announces the bytes — not around it.
    /// </summary>
    Task<IReadOnlyList<StoredFile>> GetForOwnerAsync(
        UserId ownerId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Stages the removal of the row, which is the whole of deleting a file. Call
    /// <see cref="StoredFile.Delete"/> alongside it so the bytes are reclaimed promptly.
    /// </summary>
    void Remove(StoredFile storedFile);
}

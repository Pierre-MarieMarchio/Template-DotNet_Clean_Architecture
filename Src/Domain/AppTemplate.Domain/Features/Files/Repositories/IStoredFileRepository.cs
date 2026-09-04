using AppTemplate.Domain.Features.Files.Entities;
using AppTemplate.Domain.Features.Files.ValueObjects;

namespace AppTemplate.Domain.Features.Files.Repositories;

/// <summary>
/// Aggregate access for <see cref="StoredFile"/>. Deliberately not generic, and deliberately short:
/// one method per thing a use case actually needs, in domain types only, which is what lets this
/// contract live in the Domain beside the aggregate it loads.
/// <para>
/// Two things a reader might expect here are deliberately absent. Listing a user's files is a
/// projection onto a read model with no aggregate materialised, which a <c>Queries</c> contract does
/// properly. So is the set of live object keys that the orphan sweep subtracts from the store's
/// listing: it is a column, not an aggregate, and loading a million files to read one field from
/// each would be the worst possible way to get it. The four storage words and what each promises are
/// set out in <c>CONTRIBUTING.md</c>.
/// </para>
/// <para>
/// Nothing here writes: <c>Add</c> and <c>Remove</c> stage, and <c>IUnitOfWork</c> is what commits.
/// </para>
/// </summary>
public interface IStoredFileRepository
{
    /// <returns>The file, or <c>null</c> when no file has that id.</returns>
    Task<StoredFile?> GetAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// The file filed under one object key, which is how a report arriving from the object store
    /// itself names a file — a deposit notification knows the key it wrote, not the id this system
    /// gave it.
    /// </summary>
    Task<StoredFile?> GetByObjectKeyAsync(ObjectKey objectKey, CancellationToken cancellationToken);

    /// <summary>
    /// Registrations that were never deposited against, oldest first, capped at
    /// <paramref name="batchSize"/> so one pass cannot pull an unbounded backlog into memory.
    /// <para>
    /// The coarse filter, not the decision: this says which rows are worth loading, and
    /// <see cref="StoredFile.IsAbandoned"/> is what says whether one of them may actually be given
    /// up on. The same split as the reminder feature's due query, for the same reason.
    /// </para>
    /// </summary>
    /// <param name="registeredBefore">Only files registered strictly before this instant.</param>
    Task<IReadOnlyList<StoredFile>> GetPendingRegisteredBeforeAsync(
        DateTimeOffset registeredBefore,
        int batchSize,
        CancellationToken cancellationToken);

    /// <summary>
    /// Files whose deposit is confirmed and whose content has not been examined yet, oldest first,
    /// capped at <paramref name="batchSize"/>.
    /// <para>
    /// <b>No cutoff instant, unlike the query above.</b> A registration is given time to be used; a
    /// deposit that has arrived is owed a verdict as soon as one can be had, and a delay here would
    /// only be a delay before a file its owner is waiting for becomes readable.
    /// </para>
    /// <para>
    /// Oldest first so a backlog is worked through in arrival order. A file that cannot be examined
    /// right now stays in this set and is offered again on the next pass, which is why the ordering
    /// is safe: the only conditions that are permanent — a refusal, or content nothing can examine
    /// — end in a state this query does not return, so no file can hold the front of the queue for
    /// ever.
    /// </para>
    /// </summary>
    Task<IReadOnlyList<StoredFile>> GetDepositedAsync(int batchSize, CancellationToken cancellationToken);

    void Add(StoredFile storedFile);

    /// <summary>
    /// Stages the removal of the row, which is the whole of what deleting a file means here — there
    /// is no state to move to and no flag to set. Call <see cref="StoredFile.Delete"/> alongside it
    /// so the bytes are reclaimed promptly; the sweep reclaims them either way.
    /// </summary>
    void Remove(StoredFile storedFile);
}

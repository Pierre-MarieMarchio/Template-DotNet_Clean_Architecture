using AppTemplate.Domain.Features.Files.Entities;
using AppTemplate.Infrastructure.Persistence.Common.Saving.DomainEvents;
using AppTemplate.Infrastructure.Persistence.Common.Saving.Tracking;
using AppTemplate.Infrastructure.Persistence.Features.Files.Models;

namespace AppTemplate.Infrastructure.Persistence.Features.Files.Tracking;

/// <summary>
/// The change tracker EF cannot be, for one aggregate, for the duration of one request.
/// <para>
/// EF's change tracker does three things this layer still needs, but cannot supply, because it is not
/// tracking the domain type. It is an <b>identity map</b> — ask for the same aggregate twice and get the
/// same object, so two use cases in one request cannot each hold a divergent copy. It is the <b>list of
/// things to write</b> when a save happens. And it is where the <b>domain events</b> raised during the
/// request can be found, since the aggregates that raised them are reachable from nowhere else.
/// </para>
/// <para>
/// <b>This interface inherits the other two contracts on purpose, and the registration has to match.</b>
/// One instance per request has to answer to all three — this one for the repository,
/// <see cref="IAggregateFlusher"/> for the flush interceptor, <see cref="IDomainEventSource"/> for the
/// dispatch interceptor. Registered as three independent descriptors it resolves as three objects: the
/// repository fills one identity map, the interceptor flushes a different and empty one, and every write
/// reports success while persisting nothing. <c>SharedInstanceRegistrationTests</c> is the guard, and
/// <c>PersistenceModule</c> registers the concrete type once and delegates to it from each contract.
/// </para>
/// <para>
/// Draining still matters when the row is on its way out: deleting a file raises
/// <c>StoredFileDeletedDomainEvent</c>, which is what reclaims its bytes promptly rather than at the next
/// sweep.
/// </para>
/// </summary>
internal interface IStoredFileTracker : IAggregateFlusher, IDomainEventSource
{
    /// <summary>
    /// The live aggregate already loaded in this request under <paramref name="id"/>, or <c>null</c>
    /// when there is none — including when it has been staged for deletion.
    /// </summary>
    StoredFile? Find(Guid id);

    /// <summary>Records the pairing of an aggregate with the row that stores it.</summary>
    void Track(StoredFile aggregate, StoredFileRecord record);

    /// <summary>The row an aggregate is stored in, or <c>null</c> when it is not tracked here.</summary>
    StoredFileRecord? FindRecord(Guid id);

    /// <summary>
    /// Notes that an aggregate's row has been staged for deletion: nothing more is written to it, and
    /// <see cref="Find"/> stops returning it. Its pending domain events are still drained on the next
    /// save, which is why the aggregate is passed rather than its id — one that was never loaded in this
    /// request has to be taken in here, or it would never be drained at all.
    /// </summary>
    void MarkRemoved(StoredFile aggregate, StoredFileRecord record);
}

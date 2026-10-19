using AppTemplate.Domain.Features.Files.Model.Entities;
using AppTemplate.Infrastructure.Core.Common.Saving.DomainEvents;
using AppTemplate.Infrastructure.Core.Common.Saving.Tracking;
using AppTemplate.Infrastructure.Persistence.Features.Files.Rows.Models;

namespace AppTemplate.Infrastructure.Persistence.Features.Files.Rows.Tracking;

/// <summary>
/// The change tracker EF cannot be for one aggregate during one request: an identity map, the list
/// of aggregates to write, and the source of their domain events.
/// <para>
/// It inherits <see cref="IAggregateFlusher"/> and <see cref="IDomainEventSource"/> because one
/// instance per request must answer to all three contracts: registered as three descriptors they
/// resolve as three objects, and writes would report success while persisting nothing.
/// <c>PersistenceModule</c> registers the concrete type once and delegates; <c>SharedInstanceRegistrationTests</c>
/// holds it.
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
    /// Notes that the row is staged for deletion: nothing more is written to it and <see cref="Find"/>
    /// stops returning it, but its pending events are still drained, so the aggregate is passed to be
    /// taken in even if it was never loaded in this request.
    /// </summary>
    void MarkRemoved(StoredFile aggregate, StoredFileRecord record);
}

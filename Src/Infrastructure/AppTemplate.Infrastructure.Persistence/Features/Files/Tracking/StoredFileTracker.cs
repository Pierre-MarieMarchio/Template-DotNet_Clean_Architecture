using AppTemplate.Domain.Features.Files.Entities;
using AppTemplate.Infrastructure.Persistence.Common.Saving.Tracking;
using AppTemplate.Infrastructure.Persistence.Features.Files.Mapping;
using AppTemplate.Infrastructure.Persistence.Features.Files.Models;
using Microsoft.EntityFrameworkCore;

namespace AppTemplate.Infrastructure.Persistence.Features.Files.Tracking;

/// <summary>
/// The reconciliation half of <see cref="AggregateTracker{TAggregate,TRecord}"/> for
/// <see cref="StoredFile"/>: how its aggregate's state is written onto the row EF is already tracking.
/// The identity map, the drain and the restore path that make up the other half are inherited unchanged;
/// see the base class for those.
/// </summary>
/// <remarks>
/// <para>
/// <b>Change detection.</b> EF cannot see a mutation inside an aggregate it does not track, so
/// <see cref="FlushTo"/> maps the aggregate onto the tracked row and then lets EF's own diff decide what
/// to write, rather than rebuilding the row — which would make every write a full-row <c>UPDATE</c> and
/// flatten the audit columns. A stored file has no child row to reconcile, so there is nothing beyond
/// that: every column the mapper touches is a column on the row EF is already looking at.
/// </para>
/// <para>
/// <b>Concurrency.</b> The version the aggregate is carrying is pushed into the row's <em>original</em>
/// value, which is the value EF puts in the <c>WHERE</c> clause. In the ordinary case it is already
/// equal — the aggregate got it from this very row — so this is a no-op; the point is that the guarantee
/// then holds by construction rather than by coincidence, including for an aggregate that outlived the
/// query that produced it.
/// </para>
/// <para>
/// The type is <c>internal sealed</c> and takes no contract-specific state, which is what lets
/// <c>PersistenceModule</c> register it once and resolve <see cref="IStoredFileTracker"/>,
/// <see cref="IAggregateFlusher"/> and <see cref="Common.Saving.DomainEvents.IDomainEventSource"/> to
/// that one instance.
/// </para>
/// </remarks>
internal sealed class StoredFileTracker(IStoredFileMapper mapper)
    : AggregateTracker<StoredFile, StoredFileRecord>(record => record.Version), IStoredFileTracker
{
    public override void FlushTo(DbContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        foreach (var tracked in TrackedAggregates)
        {
            var entry = context.Entry(tracked.Record);

            // A row staged for deletion, or one this context is not tracking at all, is not something
            // to write the aggregate onto. Writing to a Deleted entry would resurrect columns EF is
            // about to drop, and writing to a Detached one would silently do nothing.
            if (tracked.IsRemoved || entry.State is EntityState.Deleted or EntityState.Detached)
            {
                continue;
            }

            mapper.WriteTo(tracked.Aggregate, tracked.Record);

            if (entry.State != EntityState.Added)
            {
                // Setting the original value does not mark the property modified — EF keeps modified as
                // a flag rather than deriving it — so this only ever affects the WHERE clause.
                entry.Property(record => record.Version).OriginalValue = tracked.Aggregate.Version;
            }
        }
    }
}

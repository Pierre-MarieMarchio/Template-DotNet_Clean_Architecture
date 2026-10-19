using AppTemplate.Domain.Features.Files.Model.Entities;
using AppTemplate.Infrastructure.Core.Common.Saving.Tracking;
using AppTemplate.Infrastructure.Persistence.Features.Files.Rows.Mapping;
using AppTemplate.Infrastructure.Persistence.Features.Files.Rows.Models;
using Microsoft.EntityFrameworkCore;

namespace AppTemplate.Infrastructure.Persistence.Features.Files.Rows.Tracking;

/// <summary>
/// Writes a <see cref="StoredFile"/>'s state onto the row EF is already tracking; the identity map,
/// drain and restore path are inherited from <see cref="AggregateTracker{TAggregate,TRecord}"/>.
/// </summary>
/// <remarks>
/// EF cannot see a mutation inside an aggregate it does not track, so <see cref="FlushTo"/> maps the
/// aggregate onto the tracked row and lets EF's diff decide what to write, instead of rebuilding the
/// row, which would make every write a full-row <c>UPDATE</c> and flatten the audit columns. The
/// aggregate's version is pushed into the row's original value (the one in the <c>WHERE</c> clause),
/// so the concurrency guarantee holds by construction even for an aggregate that outlived its query.
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

            // Writing to a Deleted entry would resurrect columns EF is about to drop, to a Detached one nothing.
            if (tracked.IsRemoved || entry.State is EntityState.Deleted or EntityState.Detached)
            {
                continue;
            }

            mapper.WriteTo(tracked.Aggregate, tracked.Record);

            if (entry.State != EntityState.Added)
            {
                // Setting the original value does not mark the property modified: only the WHERE clause changes.
                entry.Property(record => record.Version).OriginalValue = tracked.Aggregate.Version;
            }
        }
    }
}

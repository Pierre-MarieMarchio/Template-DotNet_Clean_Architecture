using AppTemplate.Domain.Features.Reminders.Model.Entities;
using AppTemplate.Infrastructure.Core.Common.Saving.Tracking;
using AppTemplate.Infrastructure.Persistence.Features.Reminders.Rows.Mapping;
using AppTemplate.Infrastructure.Persistence.Features.Reminders.Rows.Models;
using Microsoft.EntityFrameworkCore;

namespace AppTemplate.Infrastructure.Persistence.Features.Reminders.Rows.Tracking;

/// <summary>The reconciliation half of <see cref="AggregateTracker{TAggregate,TRecord}"/> for
/// <see cref="Reminder"/>: how the aggregate's state is written onto the row EF is already tracking;
/// the identity map, the drain and the restore path are inherited unchanged.</summary>
/// <remarks><b>Change detection:</b> <see cref="FlushTo"/> maps the aggregate onto the tracked row
/// and lets EF's own diff decide what to write, rather than rebuilding the row — a full-row
/// <c>UPDATE</c> that would flatten the audit columns. A reminder has no child row to reconcile, so
/// there is nothing beyond that. <b>Concurrency:</b> the aggregate's version is pushed into the row's
/// <em>original</em> value — the value EF puts in the <c>WHERE</c> clause — usually already equal, so
/// the guarantee holds by construction even for an aggregate that outlived the query that produced
/// it.</remarks>
internal sealed class ReminderTracker(IReminderMapper mapper)
    : AggregateTracker<Reminder, ReminderRecord>(record => record.Version), IReminderTracker
{
    public override void FlushTo(DbContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        foreach (var tracked in TrackedAggregates)
        {
            var entry = context.Entry(tracked.Record);

            // A Deleted entry would resurrect columns EF is about to drop, and a Detached one would
            // silently take the write.
            if (tracked.IsRemoved || entry.State is EntityState.Deleted or EntityState.Detached)
            {
                continue;
            }

            mapper.WriteTo(tracked.Aggregate, tracked.Record);

            if (entry.State != EntityState.Added)
            {
                // Setting the original value does not mark the property modified, so this reaches the
                // WHERE clause only.
                entry.Property(record => record.Version).OriginalValue = tracked.Aggregate.Version;
            }
        }
    }
}

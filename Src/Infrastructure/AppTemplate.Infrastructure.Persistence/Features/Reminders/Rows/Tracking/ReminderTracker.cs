using AppTemplate.Domain.Features.Reminders.Model.Entities;
using AppTemplate.Infrastructure.Core.Common.Saving.Tracking;
using AppTemplate.Infrastructure.Persistence.Features.Reminders.Rows.Mapping;
using AppTemplate.Infrastructure.Persistence.Features.Reminders.Rows.Models;
using Microsoft.EntityFrameworkCore;

namespace AppTemplate.Infrastructure.Persistence.Features.Reminders.Rows.Tracking;

internal sealed class ReminderTracker(IReminderMapper mapper)
    : AggregateTracker<Reminder, ReminderRecord>(record => record.Version), IReminderTracker
{
    public override void FlushTo(DbContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        foreach (var tracked in TrackedAggregates)
        {
            var entry = context.Entry(tracked.Record);

            if (tracked.IsRemoved || entry.State is EntityState.Deleted or EntityState.Detached)
            {
                continue;
            }

            mapper.WriteTo(tracked.Aggregate, tracked.Record);

            if (entry.State != EntityState.Added)
            {
                entry.Property(record => record.Version).OriginalValue = tracked.Aggregate.Version;
            }
        }
    }
}

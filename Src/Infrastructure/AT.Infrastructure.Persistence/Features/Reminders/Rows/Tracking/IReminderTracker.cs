using AppTemplate.Domain.Features.Reminders.Model.Entities;
using AppTemplate.Infrastructure.Core.Common.Saving.DomainEvents;
using AppTemplate.Infrastructure.Core.Common.Saving.Tracking;
using AppTemplate.Infrastructure.Persistence.Features.Reminders.Rows.Models;

namespace AppTemplate.Infrastructure.Persistence.Features.Reminders.Rows.Tracking;

internal interface IReminderTracker : IAggregateFlusher, IDomainEventSource
{
    Reminder? Find(Guid id);

    void Track(Reminder aggregate, ReminderRecord record);

    ReminderRecord? FindRecord(Guid id);

    void MarkRemoved(Reminder aggregate, ReminderRecord record);
}

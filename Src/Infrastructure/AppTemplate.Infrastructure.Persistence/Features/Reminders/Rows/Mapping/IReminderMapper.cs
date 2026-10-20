using AppTemplate.Domain.Features.Reminders.Model.Entities;
using AppTemplate.Infrastructure.Persistence.Features.Reminders.Rows.Models;

namespace AppTemplate.Infrastructure.Persistence.Features.Reminders.Rows.Mapping;

internal interface IReminderMapper
{
    Reminder ToAggregate(ReminderRecord record);

    ReminderRecord ToNewRecord(Reminder aggregate);

    void WriteTo(Reminder aggregate, ReminderRecord record);
}

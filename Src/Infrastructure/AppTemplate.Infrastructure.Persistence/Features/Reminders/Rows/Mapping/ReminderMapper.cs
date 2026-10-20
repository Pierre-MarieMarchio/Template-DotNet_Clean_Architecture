using AppTemplate.Domain.Core.Common.Primitives;
using AppTemplate.Domain.Features.Reminders.Model.Entities;
using AppTemplate.Infrastructure.Core.Common.Saving.Tracking;
using AppTemplate.Infrastructure.Persistence.Features.Reminders.Rows.Models;

namespace AppTemplate.Infrastructure.Persistence.Features.Reminders.Rows.Mapping;

internal sealed class ReminderMapper : IReminderMapper
{
    public Reminder ToAggregate(ReminderRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);

        var aggregate = Reminder.Rehydrate(new ReminderSnapshot
        {
            Id = record.Id,
            OwnerId = UserId.Create(record.OwnerId),
            TodoListId = record.TodoListId,
            TodoItemId = record.TodoItemId,
            DueAt = record.DueAt,
            State = record.State,
            NotifiedAt = record.NotifiedAt,
            FailedAttempts = record.FailedAttempts,
            NextAttemptAt = record.NextAttemptAt,
        });

        StoredStamps.ApplyTo(aggregate, record, record.Version, record.Id, "Reminder");

        return aggregate;
    }

    public ReminderRecord ToNewRecord(Reminder aggregate)
    {
        ArgumentNullException.ThrowIfNull(aggregate);

        return new ReminderRecord
        {
            Id = aggregate.Id,
            OwnerId = aggregate.OwnerId.Value,
            TodoListId = aggregate.TodoListId,
            TodoItemId = aggregate.TodoItemId,
            DueAt = aggregate.DueAt,
            State = aggregate.State,
            NotifiedAt = aggregate.NotifiedAt,
            FailedAttempts = aggregate.FailedAttempts,
            NextAttemptAt = aggregate.NextAttemptAt,

            Version = aggregate.Version,

            CreatedAt = aggregate.CreatedAt,
            CreatedBy = aggregate.CreatedBy,
            LastModifiedAt = aggregate.LastModifiedAt,
            LastModifiedBy = aggregate.LastModifiedBy,
        };
    }

    public void WriteTo(Reminder aggregate, ReminderRecord record)
    {
        ArgumentNullException.ThrowIfNull(aggregate);
        ArgumentNullException.ThrowIfNull(record);

        record.OwnerId = aggregate.OwnerId.Value;
        record.TodoListId = aggregate.TodoListId;
        record.TodoItemId = aggregate.TodoItemId;
        record.DueAt = aggregate.DueAt;
        record.State = aggregate.State;
        record.NotifiedAt = aggregate.NotifiedAt;
        record.FailedAttempts = aggregate.FailedAttempts;
        record.NextAttemptAt = aggregate.NextAttemptAt;
    }
}

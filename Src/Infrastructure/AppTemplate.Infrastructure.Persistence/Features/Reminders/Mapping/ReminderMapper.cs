using AppTemplate.Domain.Core.Common.Primitives;
using AppTemplate.Domain.Features.Reminders.Entities;
using AppTemplate.Infrastructure.Core.Common.Saving.Tracking;
using AppTemplate.Infrastructure.Persistence.Features.Reminders.Models;

namespace AppTemplate.Infrastructure.Persistence.Features.Reminders.Mapping;

/// <summary>
/// The one place that knows both shapes.
/// <para>
/// Stateless and registered as a singleton: it touches no <c>DbContext</c>, so it can be shared, and the
/// round-trip fidelity test can exercise it with no database at all.
/// </para>
/// </summary>
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
            ClaimedAt = record.ClaimedAt,
            NotifiedAt = record.NotifiedAt,
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
            ClaimedAt = aggregate.ClaimedAt,
            NotifiedAt = aggregate.NotifiedAt,

            // Overwritten on insert, where PostgreSQL assigns xmin. Carried so the round trip stays
            // total and the fidelity test can check it.
            Version = aggregate.Version,

            // Overwritten by the audit interceptor, which runs after this.
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

        // Assigned, not replaced: EF writes a column only if the value differs from the one it read.
        record.OwnerId = aggregate.OwnerId.Value;
        record.TodoListId = aggregate.TodoListId;
        record.TodoItemId = aggregate.TodoItemId;
        record.DueAt = aggregate.DueAt;
        record.State = aggregate.State;
        record.ClaimedAt = aggregate.ClaimedAt;
        record.NotifiedAt = aggregate.NotifiedAt;

        // Version and the audit stamps are not written here: the token is PostgreSQL's, the stamps
        // are the interceptor's, and a second writer for either would eventually disagree.
    }
}

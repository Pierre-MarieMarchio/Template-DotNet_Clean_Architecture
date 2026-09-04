using AppTemplate.Domain.Features.Reminders.Entities;
using AppTemplate.Infrastructure.Persistence.Common.Saving.Tracking;
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

        var aggregate = Reminder.Rehydrate(
            record.Id,
            record.OwnerId,
            record.TodoListId,
            record.TodoItemId,
            record.DueAt,
            record.State,
            record.ClaimedAt,
            record.NotifiedAt);

        // The version and the audit stamps are read back through StoredStamps, not assigned here: the
        // aggregate exposes them as read-only properties, settable only through the explicit interfaces
        // that mark this as the persistence layer, and the four-line tail that does that is identical
        // to TodoListMapper's — see StoredStamps for why it lives there instead of in a base class.
        StoredStamps.ApplyTo(aggregate, record, record.Version, record.Id, "Reminder");

        return aggregate;
    }

    public ReminderRecord ToNewRecord(Reminder aggregate)
    {
        ArgumentNullException.ThrowIfNull(aggregate);

        return new ReminderRecord
        {
            Id = aggregate.Id,
            OwnerId = aggregate.OwnerId,
            TodoListId = aggregate.TodoListId,
            TodoItemId = aggregate.TodoItemId,
            DueAt = aggregate.DueAt,
            State = aggregate.State,
            ClaimedAt = aggregate.ClaimedAt,
            NotifiedAt = aggregate.NotifiedAt,

            // Carried even though the store owns it. On an insert PostgreSQL assigns xmin itself and EF
            // ignores whatever is here, but writing it keeps this method total — and a total method is
            // what the round-trip fidelity test can check.
            Version = aggregate.Version,

            // Likewise carried, and likewise overwritten: the audit interceptor stamps every Added entry
            // after this runs. For an aggregate being inserted these are the type's defaults; for one
            // being re-inserted after a round trip they are the values it was loaded with.
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

        // Assigned, not replaced. EF compares each value against the one it read and writes a column
        // only if it actually differs, so an unchanged aggregate produces no UPDATE at all.
        record.OwnerId = aggregate.OwnerId;
        record.TodoListId = aggregate.TodoListId;
        record.TodoItemId = aggregate.TodoItemId;
        record.DueAt = aggregate.DueAt;
        record.State = aggregate.State;
        record.ClaimedAt = aggregate.ClaimedAt;
        record.NotifiedAt = aggregate.NotifiedAt;

        // Version, CreatedAt, CreatedBy, LastModifiedAt and LastModifiedBy are deliberately NOT written
        // here. The concurrency token belongs to PostgreSQL and the audit stamps belong to the
        // interceptor; the aggregate received both on load and receives them again after each save. A
        // second writer for either would be a second opinion, and the two would eventually differ.
    }
}

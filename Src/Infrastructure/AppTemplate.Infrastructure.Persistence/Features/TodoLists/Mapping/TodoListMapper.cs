using AppTemplate.Domain.Features.TodoLists.Entities;
using AppTemplate.Infrastructure.Persistence.Common.Saving.Tracking;
using AppTemplate.Infrastructure.Persistence.Features.TodoLists.Models;

namespace AppTemplate.Infrastructure.Persistence.Features.TodoLists.Mapping;

/// <summary>
/// The one place that knows both shapes.
/// <para>
/// Read the three methods as three different jobs rather than as three overloads of "copy the fields".
/// <see cref="ToAggregate"/> is total: every stored value becomes domain state, or it is lost.
/// <see cref="ToNewRecord"/> is total in the other direction, which is what makes it the subject of the
/// round-trip fidelity test. <see cref="WriteTo"/> is deliberately <em>partial</em>: it writes the
/// columns the domain owns and leaves alone the ones the store owns, because the audit stamps and the
/// concurrency token have exactly one writer each and it is not this class.
/// </para>
/// <para>
/// Stateless and registered as a singleton, so it can safely be shared. It touches no
/// <c>DbContext</c> — reconciliation is expressed as adds and removes on plain collections, and
/// turning those into SQL is EF's job, decided by the change tracker after the fact. Keeping the
/// mapper ignorant of EF is also what lets the fidelity test exercise it with no database at all.
/// </para>
/// </summary>
internal sealed class TodoListMapper : ITodoListMapper
{
    public TodoList ToAggregate(TodoListRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);

        var items = record.Items
            .Select(item => TodoItem.Rehydrate(
                item.Id,
                item.TodoListId,
                item.Title,
                item.Description,
                item.CompletedAt,
                item.Tags.Select(tag => tag.Value)))
            .ToList();

        var aggregate = TodoList.Rehydrate(record.Id, record.OwnerId, record.Name, items);

        // The version and the audit stamps are read back through StoredStamps, not assigned here: the
        // aggregate exposes them as read-only properties, settable only through the explicit interfaces
        // that mark this as the persistence layer, and the four-line tail that does that is identical
        // to ReminderMapper's — see StoredStamps for why it lives there instead of in a base class.
        StoredStamps.ApplyTo(aggregate, record, record.Version, record.Id, "To-do list");

        return aggregate;
    }

    public TodoListRecord ToNewRecord(TodoList aggregate)
    {
        ArgumentNullException.ThrowIfNull(aggregate);

        var record = new TodoListRecord
        {
            Id = aggregate.Id,
            OwnerId = aggregate.OwnerId,
            Name = aggregate.Name.Value,

            // Carried even though the store owns it. On an insert PostgreSQL assigns xmin itself and
            // EF ignores whatever is here, but writing it keeps this method total — and a total method
            // is what the round-trip fidelity test can check. A mapper with a deliberate hole in it is
            // a mapper nobody can test for holes.
            Version = aggregate.Version,

            // Likewise carried, and likewise overwritten: the audit interceptor stamps every Added
            // entry after this runs. For an aggregate being inserted these are the type's defaults;
            // for one being re-inserted after a round trip they are the values it was loaded with.
            CreatedAt = aggregate.CreatedAt,
            CreatedBy = aggregate.CreatedBy,
            LastModifiedAt = aggregate.LastModifiedAt,
            LastModifiedBy = aggregate.LastModifiedBy,
        };

        foreach (var item in aggregate.Items)
        {
            record.Items.Add(ToNewItemRecord(item));
        }

        return record;
    }

    public bool WriteTo(TodoList aggregate, TodoListRecord record)
    {
        ArgumentNullException.ThrowIfNull(aggregate);
        ArgumentNullException.ThrowIfNull(record);

        // Assigned, not replaced. EF compares each value against the one it read and writes a column
        // only if it actually differs, so an unchanged aggregate produces no UPDATE at all.
        record.OwnerId = aggregate.OwnerId;
        record.Name = aggregate.Name.Value;

        // Version, CreatedAt, CreatedBy, LastModifiedAt and LastModifiedBy are deliberately NOT
        // written here. The concurrency token belongs to PostgreSQL and the audit stamps belong to the
        // interceptor; the aggregate received both on load and receives them again after each save.
        // A second writer for either would be a second opinion, and the two would eventually differ.
        return ReconcileItems(aggregate, record);
    }

    private static bool ReconcileItems(TodoList aggregate, TodoListRecord record)
    {
        bool structureChanged = false;

        // Indexed by id, because an item's identity is its id and nothing else — matching by position
        // or by title would turn a rename into a delete plus an insert, losing the row's history and
        // its foreign keys with it.
        var unmatched = record.Items.ToDictionary(item => item.Id);

        foreach (var item in aggregate.Items)
        {
            if (unmatched.Remove(item.Id, out var itemRecord))
            {
                structureChanged |= ReconcileTags(item, itemRecord);
            }
            else
            {
                record.Items.Add(ToNewItemRecord(item));
                structureChanged = true;
            }
        }

        // Whatever is left was removed from the aggregate. Taking it out of the tracked collection is
        // what makes EF issue the DELETE; the relationship is required and cascading, so the row's
        // tags go with it.
        foreach (var orphan in unmatched.Values)
        {
            record.Items.Remove(orphan);
            structureChanged = true;
        }

        return structureChanged;
    }

    /// <summary>
    /// A tag has no identity beyond its value, so reconciliation is set difference: values present in
    /// both sides are left completely alone — not reassigned, because reassigning the only column of a
    /// key-only row would be a no-op that still had to be reasoned about.
    /// </summary>
    private static bool ReconcileTags(TodoItem item, TodoItemRecord record)
    {
        record.Title = item.Title.Value;
        record.Description = item.Description;
        record.CompletedAt = item.CompletedAt;

        bool structureChanged = false;
        var unmatched = record.Tags.ToDictionary(tag => tag.Value, StringComparer.Ordinal);

        foreach (var tag in item.Tags)
        {
            if (!unmatched.Remove(tag.Value))
            {
                record.Tags.Add(new TodoItemTagRecord { TodoItemId = item.Id, Value = tag.Value });
                structureChanged = true;
            }
        }

        foreach (var orphan in unmatched.Values)
        {
            record.Tags.Remove(orphan);
            structureChanged = true;
        }

        return structureChanged;
    }

    private static TodoItemRecord ToNewItemRecord(TodoItem item)
    {
        var record = new TodoItemRecord
        {
            Id = item.Id,
            TodoListId = item.TodoListId,
            Title = item.Title.Value,
            Description = item.Description,
            CompletedAt = item.CompletedAt,
        };

        foreach (var tag in item.Tags)
        {
            record.Tags.Add(new TodoItemTagRecord { TodoItemId = item.Id, Value = tag.Value });
        }

        return record;
    }
}

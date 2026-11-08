using AppTemplate.Domain.Core.Common.Primitives;
using AppTemplate.Domain.Features.TodoLists.Model.Entities;
using AppTemplate.Infrastructure.Core.Common.Saving.Tracking;
using AppTemplate.Infrastructure.Persistence.Features.TodoLists.Rows.Models;

namespace AppTemplate.Infrastructure.Persistence.Features.TodoLists.Rows.Mapping;

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

        var aggregate = TodoList.Rehydrate(record.Id, UserId.Create(record.OwnerId), record.Name, items);

        StoredStamps.ApplyTo(aggregate, record, record.Version, record.Id, "To-do list");

        return aggregate;
    }

    public TodoListRecord ToNewRecord(TodoList aggregate)
    {
        ArgumentNullException.ThrowIfNull(aggregate);

        var record = new TodoListRecord
        {
            Id = aggregate.Id,
            OwnerId = aggregate.OwnerId.Value,
            Name = aggregate.Name.Value,

            Version = aggregate.Version,

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

        record.OwnerId = aggregate.OwnerId.Value;
        record.Name = aggregate.Name.Value;

        return ReconcileItems(aggregate, record);
    }

    private static bool ReconcileItems(TodoList aggregate, TodoListRecord record)
    {
        bool structureChanged = false;

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

        foreach (var orphan in unmatched.Values)
        {
            record.Items.Remove(orphan);
            structureChanged = true;
        }

        return structureChanged;
    }

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

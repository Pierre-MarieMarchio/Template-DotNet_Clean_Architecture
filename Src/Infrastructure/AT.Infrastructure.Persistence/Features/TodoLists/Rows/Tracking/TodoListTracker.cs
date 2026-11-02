using AppTemplate.Domain.Features.TodoLists.Model.Entities;
using AppTemplate.Infrastructure.Core.Common.Saving.Tracking;
using AppTemplate.Infrastructure.Persistence.Features.TodoLists.Rows.Mapping;
using AppTemplate.Infrastructure.Persistence.Features.TodoLists.Rows.Models;
using Microsoft.EntityFrameworkCore;

namespace AppTemplate.Infrastructure.Persistence.Features.TodoLists.Rows.Tracking;

internal sealed class TodoListTracker(ITodoListMapper mapper)
    : AggregateTracker<TodoList, TodoListRecord>(record => record.Version), ITodoListTracker
{
    public override void FlushTo(DbContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var rootsNeedingATouch = new HashSet<Guid>();

        foreach (var tracked in TrackedAggregates)
        {
            var entry = context.Entry(tracked.Record);

            if (tracked.IsRemoved || entry.State is EntityState.Deleted or EntityState.Detached)
            {
                continue;
            }

            if (mapper.WriteTo(tracked.Aggregate, tracked.Record))
            {
                rootsNeedingATouch.Add(tracked.Aggregate.Id);
            }

            if (entry.State != EntityState.Added)
            {
                entry.Property(record => record.Version).OriginalValue = tracked.Aggregate.Version;
            }
        }

        context.ChangeTracker.DetectChanges();

        CollectRootsWithDirtyChildren(context, rootsNeedingATouch);
        TouchRoots(context, rootsNeedingATouch);
    }

    private static void CollectRootsWithDirtyChildren(DbContext context, HashSet<Guid> rootIds)
    {
        var listIdOfItem = new Dictionary<Guid, Guid>();

        foreach (var entry in context.ChangeTracker.Entries<TodoItemRecord>())
        {
            listIdOfItem[entry.Entity.Id] = entry.Entity.TodoListId;

            if (IsDirty(entry.State))
            {
                rootIds.Add(entry.Entity.TodoListId);
            }
        }

        foreach (var entry in context.ChangeTracker.Entries<TodoItemTagRecord>())
        {
            if (IsDirty(entry.State) && listIdOfItem.TryGetValue(entry.Entity.TodoItemId, out var listId))
            {
                rootIds.Add(listId);
            }
        }
    }

    private void TouchRoots(DbContext context, HashSet<Guid> rootIds)
    {
        if (rootIds.Count == 0)
        {
            return;
        }

        foreach (var tracked in TrackedAggregates)
        {
            if (tracked.IsRemoved || !rootIds.Contains(tracked.Aggregate.Id))
            {
                continue;
            }

            var entry = context.Entry(tracked.Record);

            if (entry.State == EntityState.Unchanged)
            {
                entry.Property(record => record.LastModifiedAt).IsModified = true;
            }
        }
    }

    private static bool IsDirty(EntityState state) =>
        state is EntityState.Added or EntityState.Modified or EntityState.Deleted;
}

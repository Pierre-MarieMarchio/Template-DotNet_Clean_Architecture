using AppTemplate.Domain.Features.TodoLists.Model.Entities;
using AppTemplate.Infrastructure.Core.Common.Saving.Tracking;
using AppTemplate.Infrastructure.Persistence.Features.TodoLists.Mapping;
using AppTemplate.Infrastructure.Persistence.Features.TodoLists.Models;
using Microsoft.EntityFrameworkCore;

namespace AppTemplate.Infrastructure.Persistence.Features.TodoLists.Tracking;

/// <summary>The reconciliation half of <see cref="AggregateTracker{TAggregate,TRecord}"/> for
/// <see cref="TodoList"/>: how the aggregate's state — item and tag children included — is written
/// onto the row EF is already tracking; the identity map, the drain and the restore path are
/// inherited unchanged.</summary>
/// <remarks><b>Change detection:</b> <see cref="FlushTo"/> maps the aggregate onto the tracked row
/// and lets EF's own diff decide what to write, rather than rebuilding the row — a full-row
/// <c>UPDATE</c> that would flatten the audit columns; child rows reconcile by id, so an untouched
/// item produces no statement. <b>Aggregate boundary:</b> after mapping, every root that owns a
/// dirty item or tag row is enrolled in the write even when none of its own columns moved — what
/// makes the root's <c>xmin</c> the arbiter for the whole aggregate and moves its
/// <c>LastModifiedAt</c> when an item is added; also why <c>AggregateTracker.FlushTo</c> is not
/// shared with <see cref="AppTemplate.Infrastructure.Persistence.Features.Reminders.Tracking.ReminderTracker"/>,
/// which has no child row to enrol a root for. <b>Concurrency:</b> the aggregate's version is pushed
/// into the row's <em>original</em> value — the value EF puts in the <c>WHERE</c> clause — usually
/// already equal, so the guarantee holds by construction even for an aggregate that outlived the
/// query that produced it.</remarks>
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

            // A Deleted entry would resurrect columns EF is about to drop, and a Detached one would
            // silently take the write.
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
                // Setting the original value does not mark the property modified, so this reaches the
                // WHERE clause only.
                entry.Property(record => record.Version).OriginalValue = tracked.Aggregate.Version;
            }
        }

        // The states read below are only the real ones once EF has looked at the collections mapping
        // just changed.
        context.ChangeTracker.DetectChanges();

        CollectRootsWithDirtyChildren(context, rootsNeedingATouch);
        TouchRoots(context, rootsNeedingATouch);
    }

    /// <summary>
    /// Adds the id of every root that owns a dirty child row. Deleted children have already been taken
    /// out of their parent's collection, which is why the caller's set is pre-populated with the roots
    /// whose structure the mapper changed — those cannot be rediscovered from the graph.
    /// </summary>
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
                // One property, not the whole entry: Modified on the entry marks every column, which
                // would rewrite the root's CreatedAt and CreatedBy. One stamp is enough to make the
                // entry Modified and advance xmin.
                entry.Property(record => record.LastModifiedAt).IsModified = true;
            }
        }
    }

    private static bool IsDirty(EntityState state) =>
        state is EntityState.Added or EntityState.Modified or EntityState.Deleted;
}

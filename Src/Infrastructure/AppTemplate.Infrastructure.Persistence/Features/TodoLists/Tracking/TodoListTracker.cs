using AppTemplate.Domain.Features.TodoLists.Entities;
using AppTemplate.Infrastructure.Persistence.Common.Tracking;
using AppTemplate.Infrastructure.Persistence.Features.TodoLists.Mapping;
using AppTemplate.Infrastructure.Persistence.Features.TodoLists.Models;
using Microsoft.EntityFrameworkCore;

namespace AppTemplate.Infrastructure.Persistence.Features.TodoLists.Tracking;

/// <summary>
/// The reconciliation half of <see cref="AggregateTracker{TAggregate,TRecord}"/> for
/// <see cref="TodoList"/>: how its aggregate's state — including its item and tag children — is written
/// onto the row EF is already tracking. The identity map, the drain and the restore path that make up
/// the other half are inherited unchanged; see the base class for those.
/// </summary>
/// <remarks>
/// <para>
/// <b>Change detection.</b> EF cannot see a mutation inside an aggregate it does not track, so
/// <see cref="FlushTo"/> maps the aggregate onto the tracked row and then lets EF's own diff decide what
/// to write, rather than rebuilding the row — which would make every write a full-row <c>UPDATE</c> and
/// flatten the audit columns. Child rows are reconciled by id, so an item nobody touched is compared
/// equal and produces no statement at all.
/// </para>
/// <para>
/// <b>Aggregate boundary.</b> A change to a child is a change to its aggregate. After mapping, the
/// change tracker is asked which item and tag rows are dirty, and every root that owns one is enrolled
/// in the write even when none of its own columns moved. That is what makes the root's <c>xmin</c> the
/// arbiter for every write anywhere in the aggregate, and what moves its <c>LastModifiedAt</c> when an
/// item is added. Without it two callers could add items to the same list concurrently and the list
/// would claim it had not changed since it was created. This is also the reason
/// <c>AggregateTracker{TAggregate,TRecord}.FlushTo</c> is not shared:
/// <see cref="AppTemplate.Infrastructure.Persistence.Features.Reminders.Tracking.ReminderTracker"/>
/// has no child row to enrol a root for, so it has nothing resembling this method beyond its first few
/// lines.
/// </para>
/// <para>
/// <b>Concurrency.</b> The version the aggregate is carrying is pushed into the row's <em>original</em>
/// value, which is the value EF puts in the <c>WHERE</c> clause. In the ordinary case it is already
/// equal — the aggregate got it from this very row — so this is a no-op; the point is that the guarantee
/// then holds by construction rather than by coincidence, including for an aggregate that outlived the
/// query that produced it.
/// </para>
/// </remarks>
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

            // A row staged for deletion, or one this context is not tracking at all, is not something
            // to write the aggregate onto. Writing to a Deleted entry would resurrect columns EF is
            // about to drop, and writing to a Detached one would silently do nothing.
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
                // Setting the original value does not mark the property modified — EF keeps modified
                // as a flag rather than deriving it — so this only ever affects the WHERE clause.
                entry.Property(record => record.Version).OriginalValue = tracked.Aggregate.Version;
            }
        }

        // Mapping added and removed rows to collections EF has not looked at yet, so the states read
        // below are the real ones. Called again by the interceptor afterwards, harmlessly.
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
                // One property, not the whole entry. Setting the state to Modified marks every column
                // modified, so a change to a single item would rewrite the root's CreatedAt and
                // CreatedBy as well. Marking the stamp the audit interceptor is about to move is
                // already enough to make the entry Modified, put it in the UPDATE, and advance xmin.
                entry.Property(record => record.LastModifiedAt).IsModified = true;
            }
        }
    }

    private static bool IsDirty(EntityState state) =>
        state is EntityState.Added or EntityState.Modified or EntityState.Deleted;
}

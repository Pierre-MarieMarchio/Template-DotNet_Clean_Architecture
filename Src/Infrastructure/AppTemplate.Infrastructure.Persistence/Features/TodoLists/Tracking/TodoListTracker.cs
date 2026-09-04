using AppTemplate.Domain.Common.Abstractions;
using AppTemplate.Domain.Common.Events;
using AppTemplate.Domain.Features.TodoLists.Entities;
using AppTemplate.Infrastructure.Persistence.Features.TodoLists.Mappers;
using AppTemplate.Infrastructure.Persistence.Features.TodoLists.Models;
using Microsoft.EntityFrameworkCore;

namespace AppTemplate.Infrastructure.Persistence.Features.TodoLists.Tracking;

/// <summary>
/// Holds the to-do list aggregates in flight during one request, together with the rows they came from,
/// and reconciles the two whenever the context is saved.
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
/// would claim it had not changed since it was created.
/// </para>
/// <para>
/// <b>Concurrency.</b> The version the aggregate is carrying is pushed into the row's <em>original</em>
/// value, which is the value EF puts in the <c>WHERE</c> clause. In the ordinary case it is already
/// equal — the aggregate got it from this very row — so this is a no-op; the point is that the guarantee
/// then holds by construction rather than by coincidence, including for an aggregate that outlived the
/// query that produced it.
/// </para>
/// </remarks>
internal sealed class TodoListTracker(ITodoListMapper mapper) : ITodoListTracker
{
    private readonly Dictionary<Guid, TrackedAggregate> _tracked = [];

    /// <summary>Events drained for a save that then failed, waiting to be handed out again.</summary>
    private readonly List<IDomainEvent> _restored = [];

    public TodoList? Find(Guid id) =>
        _tracked.TryGetValue(id, out var tracked) && !tracked.IsRemoved ? tracked.Aggregate : null;

    public TodoListRecord? FindRecord(Guid id) =>
        _tracked.TryGetValue(id, out var tracked) ? tracked.Record : null;

    public void Track(TodoList aggregate, TodoListRecord record)
    {
        ArgumentNullException.ThrowIfNull(aggregate);
        ArgumentNullException.ThrowIfNull(record);

        _tracked[aggregate.Id] = new TrackedAggregate(aggregate, record);
    }

    public void MarkRemoved(TodoList aggregate, TodoListRecord record)
    {
        ArgumentNullException.ThrowIfNull(aggregate);
        ArgumentNullException.ThrowIfNull(record);

        if (_tracked.TryGetValue(aggregate.Id, out var tracked) && ReferenceEquals(tracked.Aggregate, aggregate))
        {
            tracked.IsRemoved = true;

            return;
        }

        // An aggregate reconstructed outside this request is in no identity map, and an aggregate that
        // is not tracked is never drained: the events its own deletion raised would be undeliverable.
        _tracked[aggregate.Id] = new TrackedAggregate(aggregate, record) { IsRemoved = true };
    }

    public void FlushTo(DbContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var rootsNeedingATouch = new HashSet<Guid>();

        foreach (var tracked in _tracked.Values)
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

    public void RefreshFromStore()
    {
        foreach (var tracked in _tracked.Values)
        {
            if (tracked.IsRemoved)
            {
                continue;
            }

            var record = tracked.Record;

            // The token PostgreSQL just assigned. Without this the aggregate a use case is still
            // holding would carry the version it was loaded at, and a second write in the same request
            // would fail against a token it had itself moved.
            ((IVersioned)tracked.Aggregate).SetVersion(record.Version);

            // The stamps the audit interceptor decided. The aggregate is told rather than asked: the
            // interceptor is the only writer, and this is how its decision reaches the domain object.
            ((IAuditable)tracked.Aggregate).SetCreated(record.CreatedAt, record.CreatedBy);

            if (record.LastModifiedAt is { } lastModifiedAt)
            {
                ((IAuditable)tracked.Aggregate).SetLastModified(lastModifiedAt, record.LastModifiedBy);
            }
        }
    }

    public IReadOnlyCollection<IDomainEvent> DrainDomainEvents()
    {
        List<IDomainEvent>? drained = null;

        if (_restored.Count > 0)
        {
            // First, because they were raised first: a save that failed does not reorder history.
            drained = [.. _restored];
            _restored.Clear();
        }

        foreach (var tracked in _tracked.Values)
        {
            if (tracked.Aggregate.DomainEvents.Count == 0)
            {
                continue;
            }

            drained ??= [];
            drained.AddRange(tracked.Aggregate.DomainEvents);

            // Drained, not read. An event that has been taken cannot be taken again by a later save of
            // the same aggregate in the same request, which is what makes delivery exactly-once.
            tracked.Aggregate.ClearDomainEvents();
        }

        return drained ?? [];
    }

    public void Restore(IEnumerable<IDomainEvent> domainEvents)
    {
        ArgumentNullException.ThrowIfNull(domainEvents);

        // Held here rather than pushed back into the aggregates: raising an event is the domain's own
        // act and the persistence layer has no way to perform it a second time. The next drain returns
        // them, so a retried save publishes them exactly once.
        _restored.AddRange(domainEvents);
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

        foreach (var tracked in _tracked.Values)
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

    /// <summary>One aggregate, the row it is stored in, and whether that row is on its way out.</summary>
    private sealed class TrackedAggregate(TodoList aggregate, TodoListRecord record)
    {
        internal TodoList Aggregate { get; } = aggregate;

        internal TodoListRecord Record { get; } = record;

        internal bool IsRemoved { get; set; }
    }
}

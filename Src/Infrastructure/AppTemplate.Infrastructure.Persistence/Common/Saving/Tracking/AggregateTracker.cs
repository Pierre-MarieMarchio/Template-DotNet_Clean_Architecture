using AppTemplate.Domain.Common.Abstractions;
using AppTemplate.Domain.Common.Events;
using AppTemplate.Domain.Common.Primitives;
using AppTemplate.Infrastructure.Persistence.Common.Saving.DomainEvents;
using Microsoft.EntityFrameworkCore;

namespace AppTemplate.Infrastructure.Persistence.Common.Saving.Tracking;

/// <summary>
/// The identity map, the drain and the restore path shared by every feature's aggregate tracker — the
/// part that, once <c>TodoListTracker</c> and <c>ReminderTracker</c> are placed side by side, turns out
/// to depend on nothing but <typeparamref name="TAggregate"/>'s id, its version and its audit stamps.
/// <para>
/// <b>What stays out, on purpose.</b> <see cref="FlushTo"/> is declared here and implemented nowhere:
/// mapping an aggregate onto its tracked row is exactly where the two features diverge today — one has
/// child rows to reconcile and a root to touch when only a child changed, the other does not — and
/// folding that divergence into one shared method behind a flag would hide the one part of the tracker
/// that is actually worth reading feature by feature. Below is the measurement that
/// drew this line, and for what happens the day a third tracker's <c>FlushTo</c> turns out to match one
/// of the first two exactly.
/// </para>
/// </summary>
/// <typeparam name="TAggregate">
/// The aggregate root, constrained to exactly what a tracker gets to do to it irrespective of the
/// feature: read its id from <see cref="AggregateRoot{TId}"/>, and write back the version and the audit
/// stamps the store decided.
/// </typeparam>
/// <typeparam name="TRecord">
/// The row it is stored in, constrained to the audit stamps every row carries. Its version has no shared
/// interface to hang a constraint on — the concurrency token lives on the record as a plain property,
/// not behind an abstraction — so <paramref name="version"/> supplies the one line that reads it.
/// </typeparam>
internal abstract class AggregateTracker<TAggregate, TRecord>(Func<TRecord, uint> version)
    : IAggregateFlusher, IDomainEventSource
    where TAggregate : AggregateRoot<Guid>, IVersioned, IAuditable
    where TRecord : class, IAuditable
{
    private readonly Dictionary<Guid, TrackedAggregate> _tracked = [];

    /// <summary>Events drained for a save that then failed, waiting to be handed out again.</summary>
    private readonly List<IDomainEvent> _restored = [];

    public TAggregate? Find(Guid id) =>
        _tracked.TryGetValue(id, out var tracked) && !tracked.IsRemoved ? tracked.Aggregate : null;

    public TRecord? FindRecord(Guid id) =>
        _tracked.TryGetValue(id, out var tracked) ? tracked.Record : null;

    public void Track(TAggregate aggregate, TRecord record)
    {
        ArgumentNullException.ThrowIfNull(aggregate);
        ArgumentNullException.ThrowIfNull(record);

        _tracked[aggregate.Id] = new TrackedAggregate(aggregate, record);
    }

    public void MarkRemoved(TAggregate aggregate, TRecord record)
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

    /// <summary>
    /// Writes every tracked aggregate's state onto its row and lets EF's own diff decide what to write.
    /// Left to each feature: see the type-level remarks for why.
    /// </summary>
    public abstract void FlushTo(DbContext context);

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
            ((IVersioned)tracked.Aggregate).SetVersion(version(record));

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
    /// Every aggregate this tracker is holding, for a <see cref="FlushTo"/> implementation to walk.
    /// Exposed rather than duplicated: the loop that skips a removed or untracked row is identical in
    /// every feature, and only what a live row is mapped onto differs.
    /// </summary>
    protected IReadOnlyCollection<TrackedAggregate> TrackedAggregates => _tracked.Values;

    /// <summary>One aggregate, the row it is stored in, and whether that row is on its way out.</summary>
    protected sealed class TrackedAggregate(TAggregate aggregate, TRecord record)
    {
        internal TAggregate Aggregate { get; } = aggregate;

        internal TRecord Record { get; } = record;

        internal bool IsRemoved { get; set; }
    }
}

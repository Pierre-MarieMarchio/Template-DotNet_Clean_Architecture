using AppTemplate.Domain.Core.Common.Abstractions;
using AppTemplate.Domain.Core.Common.Events;
using AppTemplate.Domain.Core.Common.Primitives;
using AppTemplate.Infrastructure.Core.Common.Saving.DomainEvents;
using Microsoft.EntityFrameworkCore;

namespace AppTemplate.Infrastructure.Core.Common.Saving.Tracking;

/// <summary>
/// The identity map, the drain and the restore path shared by every feature's aggregate tracker — the
/// part that, once <c>TodoListTracker</c> and <c>ReminderTracker</c> are placed side by side, turns out
/// to depend on nothing but <typeparamref name="TAggregate"/>'s id, its version and its audit stamps.
/// <para>
/// <b>What stays out, on purpose.</b> <see cref="FlushTo"/> is declared here and implemented nowhere:
/// mapping an aggregate onto its tracked row is exactly where the features diverge — one has child
/// rows to reconcile and a root to touch when only a child changed, the other does not — and folding
/// that divergence into one shared method behind a flag would hide the one part of a tracker that is
/// worth reading feature by feature.
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
public abstract class AggregateTracker<TAggregate, TRecord>(Func<TRecord, uint> version)
    : IAggregateFlusher, IDomainEventSource
    where TAggregate : AggregateRoot<Guid>, IVersioned, IAuditable
    where TRecord : class, IAuditable
{
    private readonly Dictionary<Guid, TrackedAggregate> _tracked = [];

    /// <summary>Events drained for a save that then failed, waiting to be handed out again.</summary>
    private readonly List<IDomainEvent> _restored = [];

    /// <summary>The aggregate this request already loaded, or <see langword="null"/>.</summary>
    /// <param name="id">The aggregate's identity.</param>
    /// <returns>The tracked aggregate, or <see langword="null"/> when it is untracked or removed.</returns>
    public TAggregate? Find(Guid id) =>
        _tracked.TryGetValue(id, out var tracked) && !tracked.IsRemoved ? tracked.Aggregate : null;

    /// <summary>The row a tracked aggregate is stored in, removed ones included.</summary>
    /// <param name="id">The aggregate's identity.</param>
    /// <returns>The tracked row, or <see langword="null"/> when nothing is tracked under that id.</returns>
    public TRecord? FindRecord(Guid id) =>
        _tracked.TryGetValue(id, out var tracked) ? tracked.Record : null;

    /// <summary>Puts an aggregate and its row in the identity map for the rest of the request.</summary>
    /// <param name="aggregate">The aggregate, as the caller holds it.</param>
    /// <param name="record">The row it is stored in.</param>
    public void Track(TAggregate aggregate, TRecord record)
    {
        ArgumentNullException.ThrowIfNull(aggregate);
        ArgumentNullException.ThrowIfNull(record);

        _tracked[aggregate.Id] = new TrackedAggregate(aggregate, record);
    }

    /// <summary>
    /// Records that this aggregate is being deleted, so it is not flushed and its events still are.
    /// </summary>
    /// <param name="aggregate">The aggregate being deleted.</param>
    /// <param name="record">The row to delete.</param>
    public void MarkRemoved(TAggregate aggregate, TRecord record)
    {
        ArgumentNullException.ThrowIfNull(aggregate);
        ArgumentNullException.ThrowIfNull(record);

        if (_tracked.TryGetValue(aggregate.Id, out var tracked) && ReferenceEquals(tracked.Aggregate, aggregate))
        {
            tracked.IsRemoved = true;

            return;
        }

        // An aggregate reconstructed outside this request is in no identity map, and an untracked one
        // is never drained, so the events its deletion raised would be undeliverable.
        _tracked[aggregate.Id] = new TrackedAggregate(aggregate, record) { IsRemoved = true };
    }

    /// <summary>
    /// Writes every tracked aggregate's state onto its row and lets EF's own diff decide what to write.
    /// Left to each feature: see the type-level remarks for why.
    /// </summary>
    public abstract void FlushTo(DbContext context);

    /// <summary>
    /// Tells every tracked aggregate the version and audit stamps the store just decided, so a
    /// second write in the same request does not fail against a token it moved itself.
    /// </summary>
    public void RefreshFromStore()
    {
        foreach (var tracked in _tracked.Values)
        {
            if (tracked.IsRemoved)
            {
                continue;
            }

            var record = tracked.Record;

            // The token PostgreSQL just assigned: without it a second write in the same request would
            // fail against a version this one had itself moved.
            ((IVersioned)tracked.Aggregate).SetVersion(version(record));

            // The stamps the audit interceptor decided, which is the only writer of them.
            ((IAuditable)tracked.Aggregate).SetCreated(record.CreatedAt, record.CreatedBy);

            if (record.LastModifiedAt is { } lastModifiedAt)
            {
                ((IAuditable)tracked.Aggregate).SetLastModified(lastModifiedAt, record.LastModifiedBy);
            }
        }
    }

    /// <summary>
    /// Takes every tracked aggregate's events, oldest first, and clears them — so an event taken
    /// once cannot be taken again by a later save in the same request.
    /// </summary>
    /// <returns>The events drained, in the order they were raised.</returns>
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

            // Drained, not read: an event taken here cannot be taken again by a later save of the
            // same aggregate in the same request.
            tracked.Aggregate.ClearDomainEvents();
        }

        return drained ?? [];
    }

    /// <summary>
    /// Hands back events drained for a save that then failed, so the next drain returns them and
    /// a retried save publishes them exactly once.
    /// </summary>
    /// <param name="domainEvents">The events the failed save had taken.</param>
    public void Restore(IEnumerable<IDomainEvent> domainEvents)
    {
        ArgumentNullException.ThrowIfNull(domainEvents);

        // Held here rather than pushed back into the aggregates, which have no way to raise an event
        // twice. The next drain returns them, so a retried save publishes them once.
        _restored.AddRange(domainEvents);
    }

    /// <summary>
    /// Every aggregate this tracker is holding, for a <see cref="FlushTo"/> implementation to walk.
    /// Exposed rather than duplicated: the loop that skips a removed or untracked row is identical in
    /// every feature, and only what a live row is mapped onto differs.
    /// </summary>
    protected IReadOnlyCollection<AggregateTracker<TAggregate, TRecord>.TrackedAggregate> TrackedAggregates =>
        _tracked.Values;

    /// <summary>One aggregate, the row it is stored in, and whether that row is on its way out.</summary>
    protected sealed class TrackedAggregate(TAggregate aggregate, TRecord record)
    {
        /// <summary>The aggregate, as the caller holds it.</summary>
        public TAggregate Aggregate { get; } = aggregate;

        /// <summary>The row it is stored in.</summary>
        public TRecord Record { get; } = record;

        /// <summary>Whether this row is on its way out, and so must not be flushed.</summary>
        public bool IsRemoved { get; set; }
    }
}

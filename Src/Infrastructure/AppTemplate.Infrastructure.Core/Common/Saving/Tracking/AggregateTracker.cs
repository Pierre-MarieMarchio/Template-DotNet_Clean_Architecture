using AppTemplate.Domain.Core.Common.Abstractions;
using AppTemplate.Domain.Core.Common.Events;
using AppTemplate.Domain.Core.Common.Primitives;
using AppTemplate.Infrastructure.Core.Common.Saving.DomainEvents;
using Microsoft.EntityFrameworkCore;

namespace AppTemplate.Infrastructure.Core.Common.Saving.Tracking;

public abstract class AggregateTracker<TAggregate, TRecord>(Func<TRecord, uint> version)
    : IAggregateFlusher, IDomainEventSource
    where TAggregate : AggregateRoot<Guid>, IVersioned, IAuditable
    where TRecord : class, IAuditable
{
    private readonly Dictionary<Guid, TrackedAggregate> _tracked = [];

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

        _tracked[aggregate.Id] = new TrackedAggregate(aggregate, record) { IsRemoved = true };
    }

    public bool IsStagedOn(DbContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return context.Model.FindEntityType(typeof(TRecord)) is not null
            && _tracked.Values.Any(
                tracked => context.Entry(tracked.Record).State != EntityState.Detached);
    }

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

            ((IVersioned)tracked.Aggregate).SetVersion(version(record));

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

            tracked.Aggregate.ClearDomainEvents();
        }

        return drained ?? [];
    }

    public void Restore(IEnumerable<IDomainEvent> domainEvents)
    {
        ArgumentNullException.ThrowIfNull(domainEvents);

        _restored.AddRange(domainEvents);
    }

    protected IReadOnlyCollection<AggregateTracker<TAggregate, TRecord>.TrackedAggregate> TrackedAggregates =>
        _tracked.Values;

    protected sealed class TrackedAggregate(TAggregate aggregate, TRecord record)
    {
        public TAggregate Aggregate { get; } = aggregate;

        public TRecord Record { get; } = record;

        public bool IsRemoved { get; set; }
    }
}

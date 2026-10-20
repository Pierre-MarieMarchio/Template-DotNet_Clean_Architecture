using AppTemplate.Domain.Core.Common.Events;

namespace AppTemplate.Domain.Core.Common.Primitives;

/// <summary>
/// Non-generic marker for an aggregate root, whatever its id type. A root's events are collected
/// through its feature's tracker, which implements <c>IDomainEventSource</c>, and not by looking
/// for this interface in a change tracker.
/// </summary>
public interface IAggregateRoot
{
    /// <summary>What the aggregate has recorded and not yet had dispatched.</summary>
    IReadOnlyCollection<IDomainEvent> DomainEvents { get; }

    /// <summary>Drops the recorded events, once something has taken them to dispatch.</summary>
    void ClearDomainEvents();
}

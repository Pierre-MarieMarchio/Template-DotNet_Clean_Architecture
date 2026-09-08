using AppTemplate.Domain.Core.Common.Events;

namespace AppTemplate.Domain.Core.Common.Primitives;

/// <summary>
/// Non-generic marker so the persistence layer can collect domain events from any root
/// without knowing its id type.
/// </summary>
public interface IAggregateRoot
{
    /// <summary>What the aggregate has recorded and not yet had dispatched.</summary>
    IReadOnlyCollection<IDomainEvent> DomainEvents { get; }

    /// <summary>Drops the recorded events, once something has taken them to dispatch.</summary>
    void ClearDomainEvents();
}

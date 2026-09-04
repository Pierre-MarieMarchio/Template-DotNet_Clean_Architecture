using AppTemplate.Domain.Common.Events;

namespace AppTemplate.Domain.Common.Primitives;

/// <summary>
/// Non-generic marker so the persistence layer can collect domain events from any root
/// without knowing its id type.
/// </summary>
public interface IAggregateRoot
{
    IReadOnlyCollection<IDomainEvent> DomainEvents { get; }

    void ClearDomainEvents();
}

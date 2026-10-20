using AppTemplate.Domain.Core.Common.Events;

namespace AppTemplate.Domain.Core.Common.Primitives;

public interface IAggregateRoot
{
    IReadOnlyCollection<IDomainEvent> DomainEvents { get; }

    void ClearDomainEvents();
}

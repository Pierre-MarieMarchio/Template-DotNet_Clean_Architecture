using AppTemplate.Domain.Common.Events;

namespace AppTemplate.Infrastructure.Persistence.Common.Saving.DomainEvents;

/// <summary>Publishes a domain event to every consumer registered for its concrete type.</summary>
internal interface IDomainEventDispatcher
{
    Task DispatchAsync(IDomainEvent domainEvent, CancellationToken cancellationToken = default);
}

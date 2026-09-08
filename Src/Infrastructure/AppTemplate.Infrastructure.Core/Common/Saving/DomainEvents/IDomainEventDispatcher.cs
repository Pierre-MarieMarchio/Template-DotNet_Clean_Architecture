using AppTemplate.Domain.Core.Common.Events;

namespace AppTemplate.Infrastructure.Core.Common.Saving.DomainEvents;

/// <summary>Publishes a domain event to every consumer registered for its concrete type.</summary>
public interface IDomainEventDispatcher
{
    /// <summary>Hands one event to whatever consumes its type.</summary>
    /// <param name="domainEvent">The event to deliver.</param>
    /// <param name="cancellationToken">Cancels the delivery.</param>
    Task DispatchAsync(IDomainEvent domainEvent, CancellationToken cancellationToken = default);
}

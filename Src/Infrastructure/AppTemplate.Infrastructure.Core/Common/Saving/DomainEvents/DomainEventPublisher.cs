using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Domain.Core.Common.Events;

namespace AppTemplate.Infrastructure.Core.Common.Saving.DomainEvents;

/// <summary>
/// <see cref="IDomainEventPublisher"/> over the same dispatcher the save interceptor publishes
/// through, so a fact published by hand reaches exactly the consumers an aggregate's event does.
/// </summary>
/// <remarks>
/// A class of its own rather than a second interface on <see cref="DomainEventDispatcher"/> because
/// the contracts differ in what they promise: the interceptor publishes after a commit it watched,
/// while this port's caller owns the ordering and is trusted to publish only what already committed
/// — a promise worth stating on the port a use case reads, not buried in a dispatcher's own doc.
/// </remarks>
internal sealed class DomainEventPublisher(IDomainEventDispatcher dispatcher) : IDomainEventPublisher
{
    public Task PublishAsync(IDomainEvent domainEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);

        return dispatcher.DispatchAsync(domainEvent, cancellationToken);
    }
}

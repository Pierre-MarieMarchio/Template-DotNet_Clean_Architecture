using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Domain.Core.Common.Events;

namespace AppTemplate.Infrastructure.Core.Common.Saving.DomainEvents;

internal sealed class DomainEventPublisher(IDomainEventDispatcher dispatcher) : IDomainEventPublisher
{
    public Task PublishAsync(IDomainEvent domainEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);

        return dispatcher.DispatchAsync(domainEvent, cancellationToken);
    }
}

using AppTemplate.Domain.Core.Common.Events;

namespace AppTemplate.Application.Core.Common.Ports;

public interface IDomainEventPublisher
{
    Task PublishAsync(IDomainEvent domainEvent, CancellationToken cancellationToken = default);
}

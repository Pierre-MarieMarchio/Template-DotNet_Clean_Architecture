using AppTemplate.Domain.Core.Common.Events;

namespace AppTemplate.Infrastructure.Core.Common.Saving.DomainEvents;

public interface IDomainEventDispatcher
{
    Task DispatchAsync(IDomainEvent domainEvent, CancellationToken cancellationToken = default);
}

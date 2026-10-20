using AppTemplate.Domain.Core.Common.Events;

namespace AppTemplate.Application.Core.Common.Events;

public interface IDomainEventConsumer
{
    Task ConsumeAsync(IDomainEvent domainEvent, CancellationToken cancellationToken = default);
}

public interface IDomainEventConsumer<in TEvent> : IDomainEventConsumer
    where TEvent : IDomainEvent
{
    Task ConsumeAsync(TEvent domainEvent, CancellationToken cancellationToken = default);

    Task IDomainEventConsumer.ConsumeAsync(IDomainEvent domainEvent, CancellationToken cancellationToken)
    {
        if (domainEvent is TEvent typed)
        {
            return ConsumeAsync(typed, cancellationToken);
        }

        throw new ArgumentException(
            $"'{GetType().Name}' consumes '{typeof(TEvent).Name}' but was handed " +
            $"'{domainEvent?.GetType().Name ?? "null"}'.",
            nameof(domainEvent));
    }
}

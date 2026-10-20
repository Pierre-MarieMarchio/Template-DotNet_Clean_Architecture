using AppTemplate.Application.Core.Common.Events;
using AppTemplate.Domain.Core.Common.Events;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AppTemplate.Infrastructure.Core.Common.Saving.DomainEvents;

public sealed class DomainEventDispatcher(
    IServiceProvider serviceProvider,
    ILogger<DomainEventDispatcher> logger) : IDomainEventDispatcher
{
    public async Task DispatchAsync(IDomainEvent domainEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);

        var consumerType = typeof(IDomainEventConsumer<>).MakeGenericType(domainEvent.GetType());

        foreach (object? consumer in serviceProvider.GetServices(consumerType))
        {
            if (consumer is not IDomainEventConsumer typedConsumer)
            {
                throw new InvalidOperationException(
                    $"'{consumer?.GetType().FullName ?? "null"}' is registered as a consumer of "
                    + $"'{domainEvent.GetType().Name}' but is not an {nameof(IDomainEventConsumer)}.");
            }

            try
            {
                await typedConsumer.ConsumeAsync(domainEvent, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                logger.LogError(
                    exception,
                    "Consumer {ConsumerType} of domain event {DomainEventType} threw and was skipped. " +
                    "Its side effect for this event did not happen and will not be retried.",
                    typedConsumer.GetType().Name,
                    domainEvent.GetType().Name);
            }
        }
    }
}

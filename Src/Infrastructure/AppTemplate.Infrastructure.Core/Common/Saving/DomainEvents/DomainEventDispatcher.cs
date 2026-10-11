using AppTemplate.Application.Core.Common.Events;
using AppTemplate.Domain.Core.Common.Events;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AppTemplate.Infrastructure.Core.Common.Saving.DomainEvents;

/// <summary>Resolves consumers straight from the container — no mediator library: the mechanism is
/// a dictionary lookup and a loop, and a dependency hiding that costs more in indirection than it
/// saves in code. Internal, with its interface: dispatch happens as part of committing and this
/// assembly owns that moment. <b>Isolation, not durability:</b> each consumer runs in its own
/// <c>try</c>/<c>catch</c>, so one exception cannot stop a sibling consumer of the same event — the
/// entire guarantee. The throwing consumer's side effect is still lost; no retry, no outbox,
/// deliberately (docs/DECISIONS.md).</summary>
public sealed class DomainEventDispatcher(
    IServiceProvider serviceProvider,
    ILogger<DomainEventDispatcher> logger) : IDomainEventDispatcher
{
    /// <summary>Hands one event to every consumer registered for its type.</summary>
    /// <param name="domainEvent">The event to deliver.</param>
    /// <param name="cancellationToken">Cancels the delivery.</param>
    public async Task DispatchAsync(IDomainEvent domainEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);

        // The closed generic is built from the event's runtime type, so a consumer is found
        // for the event that was actually raised rather than for the interface it is held as.
        var consumerType = typeof(IDomainEventConsumer<>).MakeGenericType(domainEvent.GetType());

        foreach (object? consumer in serviceProvider.GetServices(consumerType))
        {
            // Unreachable through the interface hierarchy, and thrown outside the try/catch below so
            // that a registration making it reachable is loud rather than a skipped side effect.
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
                // A cancelled request is not a failed consumer.
                throw;
            }
            catch (Exception exception)
            {
                // Isolation, not durability: this consumer's side effect is lost and every
                // remaining consumer of this event still runs.
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

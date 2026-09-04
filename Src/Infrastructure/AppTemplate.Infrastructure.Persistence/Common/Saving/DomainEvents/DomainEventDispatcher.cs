using AppTemplate.Application.Common.Events;
using AppTemplate.Domain.Common.Events;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AppTemplate.Infrastructure.Persistence.Common.Saving.DomainEvents;

/// <summary>
/// Resolves consumers straight from the container. No mediator library: the whole mechanism
/// is a dictionary lookup and a loop, and a dependency whose job is to hide that costs more
/// in indirection than it saves in code.
/// <para>
/// Internal, together with its interface: dispatch happens as part of committing a
/// transaction and this assembly owns that moment. A feature or a host supplies consumers, never
/// the dispatcher.
/// </para>
/// <para>
/// <b>Isolation, not durability.</b> Each consumer of an event runs in its own <c>try</c>/<c>catch</c>,
/// so one consumer's exception cannot stop a sibling consumer of the <em>same</em> event from running.
/// That is the entire guarantee: the throwing consumer's own side effect is still lost, there is no
/// retry, and there is no outbox — deliberately, since the operational half of one (a dispatcher, a
/// dead-letter queue, an alert on lag) has no default a template can ship;
/// this is the narrower, deliberately incomplete half of that decision.
/// </para>
/// </summary>
internal sealed class DomainEventDispatcher(
    IServiceProvider serviceProvider,
    ILogger<DomainEventDispatcher> logger) : IDomainEventDispatcher
{
    public async Task DispatchAsync(IDomainEvent domainEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);

        // The closed generic is built from the event's runtime type, so a consumer is found
        // for the event that was actually raised rather than for the interface it is held as.
        var consumerType = typeof(IDomainEventConsumer<>).MakeGenericType(domainEvent.GetType());

        foreach (object? consumer in serviceProvider.GetServices(consumerType))
        {
            // Unreachable through the interface hierarchy, and loud rather than silent if a
            // registration ever makes it reachable: skipping a consumer in a mechanism whose whole
            // job is a side effect is the one failure nobody would notice. Thrown outside the
            // try/catch below, so this isolation cannot turn this composition bug into a silent
            // no-op.
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
                // A cancelled request is not a failed consumer: rethrowing keeps cancellation
                // honest instead of logging every cancelled request as a consumer failure.
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

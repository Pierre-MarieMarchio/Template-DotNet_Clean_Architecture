using AppTemplate.Application.Common.Abstractions;
using AppTemplate.Domain.Common.Events;
using Microsoft.Extensions.DependencyInjection;

namespace AppTemplate.Infrastructure.Persistence.Common.DomainEvents;

/// <summary>
/// Resolves consumers straight from the container. No mediator library: the whole mechanism
/// is a dictionary lookup and a loop, and a dependency whose job is to hide that costs more
/// in indirection than it saves in code.
/// <para>
/// Internal, together with its interface: dispatch happens as part of committing a
/// transaction and this assembly owns that moment. A feature or a host supplies consumers, never
/// the dispatcher.
/// </para>
/// </summary>
internal sealed class DomainEventDispatcher(IServiceProvider serviceProvider) : IDomainEventDispatcher
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
            // job is a side effect is the one failure nobody would notice.
            if (consumer is not IDomainEventConsumer typedConsumer)
            {
                throw new InvalidOperationException(
                    $"'{consumer?.GetType().FullName ?? "null"}' is registered as a consumer of "
                    + $"'{domainEvent.GetType().Name}' but is not an {nameof(IDomainEventConsumer)}.");
            }

            await typedConsumer.ConsumeAsync(domainEvent, cancellationToken);
        }
    }
}

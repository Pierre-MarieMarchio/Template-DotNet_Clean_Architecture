using AppTemplate.Domain.Core.Common.Events;

namespace AppTemplate.Application.Core.Common.Events;

/// <summary>
/// Non-generic entry point used by the dispatcher, so dispatching a heterogeneous batch needs no
/// reflective invocation. Implement <see cref="IDomainEventConsumer{TEvent}"/> instead of this.
/// </summary>
public interface IDomainEventConsumer
{
    /// <summary>
    /// What the dispatcher calls for an event of any type; the generic interface implements it by
    /// type-checking and delegating.
    /// </summary>
    Task ConsumeAsync(IDomainEvent domainEvent, CancellationToken cancellationToken = default);
}

/// <summary>
/// Reacts to one kind of domain event, after the transaction that raised it has committed. Several
/// consumers may react to the same event and all of them run.
/// </summary>
/// <remarks>
/// Named <em>consumer</em> rather than <em>handler</em> because CA1711 reserves the
/// <c>EventHandler</c> suffix for delegates, and a reader who sees that suffix on a public type is
/// entitled to expect the .NET event pattern, which this is not.
/// </remarks>
/// <typeparam name="TEvent">The concrete event type this consumer reacts to.</typeparam>
public interface IDomainEventConsumer<in TEvent> : IDomainEventConsumer
    where TEvent : IDomainEvent
{
    /// <summary>
    /// The reaction itself. It runs after the raising transaction has committed, so throwing here cannot
    /// undo the change that caused it — a consumer has to answer its own failures.
    /// </summary>
    Task ConsumeAsync(TEvent domainEvent, CancellationToken cancellationToken = default);

    /// <remarks>
    /// A consumer reacting to two event types cannot inherit this bridge twice — the call would be
    /// ambiguous. Implement <see cref="IDomainEventConsumer"/> directly and switch on the event, or
    /// write one consumer per event type.
    /// </remarks>
    Task IDomainEventConsumer.ConsumeAsync(IDomainEvent domainEvent, CancellationToken cancellationToken)
    {
        if (domainEvent is TEvent typed)
        {
            return ConsumeAsync(typed, cancellationToken);
        }

        // A bare InvalidCastException here names neither the consumer nor the event, which is the
        // one thing needed to find the mis-registration that caused it.
        throw new ArgumentException(
            $"'{GetType().Name}' consumes '{typeof(TEvent).Name}' but was handed " +
            $"'{domainEvent?.GetType().Name ?? "null"}'.",
            nameof(domainEvent));
    }
}

using AppTemplate.Domain.Core.Common.Events;

namespace AppTemplate.Application.Core.Common.Ports;

/// <summary>
/// Publishes a fact to every consumer registered for its type, the publication half of what
/// <see cref="AppTemplate.Application.Core.Common.Events.IDomainEventConsumer{TEvent}"/> consumes.
/// </summary>
/// <remarks><b>The caller owns the ordering:</b> a fact no aggregate carries is published here, by
/// the operation that made it true, and publishing before the write commits hands a consumer a fact
/// the store can still deny. <b>In-process, no retry, no outbox:</b> a consumer that throws is
/// logged and skipped; when a missed fact must not be lost, the guarantee behind it is the sweep.</remarks>
public interface IDomainEventPublisher
{
    /// <summary>Hands <paramref name="domainEvent"/> to every consumer registered for its concrete
    /// type; returns when they have all run.</summary>
    /// <param name="domainEvent">The fact to publish — already true: committed, or true for reasons
    /// no transaction can undo.</param>
    /// <param name="cancellationToken">Cancels the delivery.</param>
    Task PublishAsync(IDomainEvent domainEvent, CancellationToken cancellationToken = default);
}

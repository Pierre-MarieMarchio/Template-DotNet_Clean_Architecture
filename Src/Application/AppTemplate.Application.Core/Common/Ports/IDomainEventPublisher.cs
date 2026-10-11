using AppTemplate.Domain.Core.Common.Events;

namespace AppTemplate.Application.Core.Common.Ports;

/// <summary>
/// Publishes a fact to every consumer registered for its type, the publication half of what
/// <see cref="AppTemplate.Application.Core.Common.Events.IDomainEventConsumer{TEvent}"/> consumes.
/// </summary>
/// <remarks>
/// <b>The caller owns the ordering, and must honour it.</b> The save interceptor publishes
/// automatically, but only what an aggregate raised in a save it intercepted; a fact no aggregate
/// carries — a deleted account, for instance — is published through this port, by the operation
/// that made it true, and publishing before the write has committed hands a consumer a fact the
/// store can still deny. <b>In-process, no retry, no outbox:</b> a consumer that throws is logged
/// and skipped; when a missed fact must not be merely lost, the guarantee behind it is the sweep,
/// not this port.
/// </remarks>
public interface IDomainEventPublisher
{
    /// <summary>
    /// Hands <paramref name="domainEvent"/> to every consumer registered for its concrete type.
    /// Returns when they have all run.
    /// </summary>
    /// <param name="domainEvent">The fact to publish. It must already be true: committed, or true
    /// for reasons no transaction can undo.</param>
    /// <param name="cancellationToken">Cancels the delivery.</param>
    Task PublishAsync(IDomainEvent domainEvent, CancellationToken cancellationToken = default);
}

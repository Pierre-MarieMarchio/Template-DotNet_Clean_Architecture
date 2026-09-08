using AppTemplate.Domain.Core.Common.Events;

namespace AppTemplate.Domain.Core.Common.Primitives;

/// <summary>
/// The consistency and transactional boundary: only aggregate roots get a repository, and
/// entities inside an aggregate are reached through their root.
/// </summary>
/// <typeparam name="TId">The identity type, which may not be null.</typeparam>
public abstract class AggregateRoot<TId> : Entity<TId>, IAggregateRoot
    where TId : notnull
{
    private readonly List<IDomainEvent> _domainEvents = [];

    /// <summary>Assigns the identity, which an aggregate never afterwards changes.</summary>
    /// <param name="id">The identity. Refused when null.</param>
    protected AggregateRoot(TId id) : base(id)
    {
    }

    /// <inheritdoc />
    public IReadOnlyCollection<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    /// <summary>
    /// Records something the model decided. Protected on purpose: an event is raised by the
    /// aggregate that owns the decision, never by a caller reaching in from outside.
    /// </summary>
    /// <param name="domainEvent">What happened. Refused when null.</param>
    protected void RaiseDomainEvent(IDomainEvent domainEvent)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);
        _domainEvents.Add(domainEvent);
    }

    /// <inheritdoc />
    public void ClearDomainEvents() => _domainEvents.Clear();
}

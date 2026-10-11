using AppTemplate.Domain.Core.Common.Abstractions;

namespace AppTemplate.Domain.Core.Common.Primitives;

/// <summary>
/// An aggregate root whose row carries the two stamps the store owns: the audit values and the
/// optimistic-concurrency token. Both are declared here because the shape is the same every time —
/// a getter anyone may read, and a setter reachable only by casting to <see cref="IAuditable"/> or
/// <see cref="IVersioned"/>, so code that wants to forge either has to say so first. An aggregate
/// that needs neither derives from <see cref="AggregateRoot{TId}"/> instead.
/// </summary>
/// <typeparam name="TId">The identity type, which may not be null.</typeparam>
public abstract class AuditableAggregateRoot<TId> : AggregateRoot<TId>, IAuditable, IVersioned
    where TId : notnull
{
    /// <summary>Assigns the identity, which an aggregate never afterwards changes.</summary>
    /// <param name="id">The identity. Refused when null.</param>
    protected AuditableAggregateRoot(TId id) : base(id)
    {
    }

    /// <summary>
    /// Optimistic concurrency token, owned and replaced by the store on every write. It lives on
    /// the root and nowhere below it, because the root is the consistency boundary.
    /// </summary>
    public uint Version { get; private set; }

    /// <inheritdoc />
    public DateTimeOffset CreatedAt { get; private set; }

    /// <inheritdoc />
    public Guid? CreatedBy { get; private set; }

    /// <inheritdoc />
    public DateTimeOffset? LastModifiedAt { get; private set; }

    /// <inheritdoc />
    public Guid? LastModifiedBy { get; private set; }

    void IAuditable.SetCreated(DateTimeOffset at, Guid? by)
    {
        CreatedAt = at;
        CreatedBy = by;
    }

    void IAuditable.SetLastModified(DateTimeOffset at, Guid? by)
    {
        LastModifiedAt = at;
        LastModifiedBy = by;
    }

    void IVersioned.SetVersion(uint version) => Version = version;
}

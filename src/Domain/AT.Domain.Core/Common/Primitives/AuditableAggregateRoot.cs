using AppTemplate.Domain.Core.Common.Abstractions;

namespace AppTemplate.Domain.Core.Common.Primitives;

public abstract class AuditableAggregateRoot<TId> : AggregateRoot<TId>, IAuditable, IVersioned
    where TId : notnull
{
    protected AuditableAggregateRoot(TId id) : base(id)
    {
    }

    public uint Version { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public Guid? CreatedBy { get; private set; }

    public DateTimeOffset? LastModifiedAt { get; private set; }

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

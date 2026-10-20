using AppTemplate.Domain.Core.Common.Abstractions;

namespace AppTemplate.Infrastructure.Core.Common.Saving.Tracking;

public static class StoredStamps
{
    public static void ApplyTo<TAggregate>(
        TAggregate aggregate,
        IAuditable record,
        uint version,
        Guid recordId,
        string aggregateName)
        where TAggregate : IVersioned, IAuditable
    {
        ArgumentNullException.ThrowIfNull(aggregate);
        ArgumentNullException.ThrowIfNull(record);

        aggregate.SetVersion(version);
        aggregate.SetCreated(record.CreatedAt, record.CreatedBy);

        if (record.LastModifiedAt is { } lastModifiedAt)
        {
            aggregate.SetLastModified(lastModifiedAt, record.LastModifiedBy);
        }
        else if (record.LastModifiedBy is not null)
        {
            throw new InvalidOperationException(
                $"{aggregateName} '{recordId}' records a last modifier but no modification time. The "
                + "audit columns are written as a pair, so this row was changed by something else.");
        }
    }
}

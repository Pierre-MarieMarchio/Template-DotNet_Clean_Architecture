namespace AppTemplate.Domain.Core.Common.Abstractions;

public interface IAuditable
{
    DateTimeOffset CreatedAt { get; }

    Guid? CreatedBy { get; }

    DateTimeOffset? LastModifiedAt { get; }

    Guid? LastModifiedBy { get; }

    void SetCreated(DateTimeOffset at, Guid? by);

    void SetLastModified(DateTimeOffset at, Guid? by);
}

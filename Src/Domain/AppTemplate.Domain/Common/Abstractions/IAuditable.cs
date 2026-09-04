namespace AppTemplate.Domain.Common.Abstractions;

/// <summary>
/// Opt-in audit stamping, applied by a persistence interceptor. Implementations are expected
/// to implement the setters explicitly, so application code cannot forge audit values.
/// </summary>
public interface IAuditable
{
    DateTimeOffset CreatedAt { get; }

    Guid? CreatedBy { get; }

    DateTimeOffset? LastModifiedAt { get; }

    Guid? LastModifiedBy { get; }

    void SetCreated(DateTimeOffset at, Guid? by);

    void SetLastModified(DateTimeOffset at, Guid? by);
}

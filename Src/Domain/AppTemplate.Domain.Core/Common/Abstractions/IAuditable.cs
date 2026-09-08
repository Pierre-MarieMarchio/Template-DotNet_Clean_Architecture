namespace AppTemplate.Domain.Core.Common.Abstractions;

/// <summary>
/// Opt-in audit stamping, applied by a persistence interceptor. Implementations are expected
/// to implement the setters explicitly, so application code cannot forge audit values.
/// </summary>
public interface IAuditable
{
    /// <summary>When the entity was first persisted.</summary>
    DateTimeOffset CreatedAt { get; }

    /// <summary>Who created it, or <see langword="null"/> when no caller was attributable.</summary>
    Guid? CreatedBy { get; }

    /// <summary>When it last changed, or <see langword="null"/> while it never has.</summary>
    DateTimeOffset? LastModifiedAt { get; }

    /// <summary>Who last changed it, or <see langword="null"/> when no caller was attributable.</summary>
    Guid? LastModifiedBy { get; }

    /// <summary>Stamps the creation values. Called once, by the interceptor, on insert.</summary>
    /// <param name="at">The instant of the insert.</param>
    /// <param name="by">The caller, or <see langword="null"/> when there is none to attribute.</param>
    void SetCreated(DateTimeOffset at, Guid? by);

    /// <summary>Stamps the modification values. Called by the interceptor on every update.</summary>
    /// <param name="at">The instant of the update.</param>
    /// <param name="by">The caller, or <see langword="null"/> when there is none to attribute.</param>
    void SetLastModified(DateTimeOffset at, Guid? by);
}

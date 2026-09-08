namespace AppTemplate.Application.Core.Common.Concurrency;

/// <summary>
/// A value read out of the store together with the concurrency token the aggregate it came from
/// held at that moment.
/// </summary>
/// <remarks>
/// The token travels beside the value rather than inside it. A caller that needs to publish a
/// validator has it; a caller that only needs the representation ignores it; and the DTO stays a
/// description of the resource, with no field a client could mistake for part of the domain.
/// </remarks>
/// <param name="Version">The aggregate's version, not the value's.</param>
public sealed record Versioned<TValue>(TValue Value, uint Version) where TValue : notnull
{
    /// <summary>
    /// Never null, so a caller holding the pair never has to decide what a version without a value means.
    /// </summary>
    public TValue Value { get; } = Value ?? throw new ArgumentNullException(nameof(Value));
}

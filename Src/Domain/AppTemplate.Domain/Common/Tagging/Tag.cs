using AppTemplate.Domain.Core.Common.Exceptions;

namespace AppTemplate.Domain.Common.Tagging;

/// <summary>
/// A free-text label a caller puts on something it owns. Normalised on the way in (trimmed,
/// lower-cased) so that "Urgent", "urgent " and "URGENT" are one tag, which is what makes the
/// de-duplication in <see cref="TagSet"/> reliable.
/// <para>
/// Shared rather than owned by one feature: a to-do item and a stored file are both tagged, and
/// they are tagged by the same rule. What is per-aggregate is how many tags it will carry, which
/// is <see cref="TagSet"/>'s parameter and not this type's business.
/// </para>
/// </summary>
public sealed record Tag
{
    public const int MaxLength = 50;

    private Tag(string value) => Value = value;

    public string Value { get; }

    /// <exception cref="DomainException">The value is blank or too long.</exception>
    public static Tag Create(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new DomainException("A tag cannot be empty.");
        }

        string normalised = value.Trim().ToLowerInvariant();

        if (normalised.Length > MaxLength)
        {
            throw new DomainException($"A tag cannot exceed {MaxLength} characters.");
        }

        return new Tag(normalised);
    }

    public override string ToString() => Value;
}

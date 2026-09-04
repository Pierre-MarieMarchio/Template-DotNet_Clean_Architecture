using AppTemplate.Domain.Common.Exceptions;

namespace AppTemplate.Domain.Features.TodoLists.ValueObjects;

/// <summary>
/// A free-text label on a <see cref="TodoItem"/>. Normalised on the way in (trimmed,
/// lower-cased) so that "Urgent", "urgent " and "URGENT" are one tag, which is what makes
/// de-duplication and filtering by tag reliable.
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

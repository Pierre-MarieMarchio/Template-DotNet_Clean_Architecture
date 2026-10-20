using AppTemplate.Domain.Core.Common.Exceptions;

namespace AppTemplate.Domain.Common.Tagging;

public sealed record Tag
{
    public const int MaxLength = 50;

    private Tag(string value) => Value = value;

    public string Value { get; }

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

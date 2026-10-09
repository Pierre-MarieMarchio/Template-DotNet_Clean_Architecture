using AppTemplate.Domain.Core.Common.Exceptions;

namespace AppTemplate.Domain.Features.Files.ValueObjects;

/// <summary>
/// The name of the category a file was registered under: a lower-case slug of <c>a-z</c>,
/// <c>0-9</c> and <c>-</c>, 1 to <see cref="MaxLength"/> characters. What a category allows is
/// configuration, read by the application; the domain only keeps the name.
/// </summary>
public sealed record FileCategory
{
    /// <summary>The longest name a category may have.</summary>
    public const int MaxLength = 64;

    private FileCategory(string value) => Value = value;

    /// <summary>The slug, exactly as declared.</summary>
    public string Value { get; }

    /// <summary>Whether <paramref name="value"/> is a well-formed category name.</summary>
    public static bool IsValid(string? value) =>
        !string.IsNullOrEmpty(value)
        && value.Length <= MaxLength
        && value.All(character => char.IsAsciiLetterLower(character) || char.IsAsciiDigit(character) || character == '-');

    /// <exception cref="DomainException">The value is not a lower-case slug of 1 to 64 characters.</exception>
    public static FileCategory Create(string value)
    {
        if (!IsValid(value))
        {
            throw new DomainException(
                $"A file category is 1 to {MaxLength} characters of lower-case letters, digits and '-'.");
        }

        return new FileCategory(value);
    }

    /// <inheritdoc />
    public override string ToString() => Value;
}

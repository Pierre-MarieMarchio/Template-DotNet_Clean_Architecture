using AppTemplate.Domain.Core.Common.Exceptions;

namespace AppTemplate.Domain.Features.Files.Model.ValueObjects;

public sealed record FileCategory
{
    public const int MaxLength = 64;

    private FileCategory(string value) => Value = value;

    public string Value { get; }

    public static bool IsValid(string? value) =>
        !string.IsNullOrEmpty(value)
        && value.Length <= MaxLength
        && value.All(character => char.IsAsciiLetterLower(character) || char.IsAsciiDigit(character) || character == '-');

    public static FileCategory Create(string value)
    {
        if (!IsValid(value))
        {
            throw new DomainException(
                $"A file category is 1 to {MaxLength} characters of lower-case letters, digits and '-'.");
        }

        return new FileCategory(value);
    }

    public override string ToString() => Value;
}

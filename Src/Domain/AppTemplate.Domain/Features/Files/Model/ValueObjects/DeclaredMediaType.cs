using AppTemplate.Domain.Core.Common.Exceptions;

namespace AppTemplate.Domain.Features.Files.Model.ValueObjects;

public sealed record DeclaredMediaType
{
    public const int MaxTokenLength = 127;

    public const int MaxLength = (MaxTokenLength * 2) + 1;

    private DeclaredMediaType(string type, string subtype)
    {
        Type = type;
        Subtype = subtype;
        Value = $"{type}/{subtype}";
    }

    public string Value { get; }

    public string Type { get; }

    public string Subtype { get; }

    public static DeclaredMediaType Create(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new DomainException("A media type cannot be empty.");
        }

        string normalised = value.Trim().ToLowerInvariant();

        int separator = normalised.IndexOf('/');

        if (separator < 0 || normalised.IndexOf('/', separator + 1) >= 0)
        {
            throw new DomainException("A media type must be written as 'type/subtype'.");
        }

        string type = normalised[..separator];
        string subtype = normalised[(separator + 1)..];

        if (type.Length == 0 || subtype.Length == 0)
        {
            throw new DomainException("A media type must have both a type and a subtype.");
        }

        if (type.Length > MaxTokenLength || subtype.Length > MaxTokenLength)
        {
            throw new DomainException($"Neither half of a media type may exceed {MaxTokenLength} characters.");
        }

        if (!IsToken(type) || !IsToken(subtype))
        {
            throw new DomainException("A media type may only contain token characters.");
        }

        if (type == "*" || subtype == "*")
        {
            throw new DomainException("A media type cannot be a wildcard.");
        }

        return new DeclaredMediaType(type, subtype);
    }

    public override string ToString() => Value;

    private static bool IsToken(string token)
    {
        foreach (char character in token)
        {
            bool allowed = char.IsAsciiLetterLower(character)
                || char.IsAsciiDigit(character)
                || character is '!' or '#' or '$' or '%' or '&' or '\'' or '*'
                    or '+' or '-' or '.' or '^' or '_' or '`' or '|' or '~';

            if (!allowed)
            {
                return false;
            }
        }

        return true;
    }
}

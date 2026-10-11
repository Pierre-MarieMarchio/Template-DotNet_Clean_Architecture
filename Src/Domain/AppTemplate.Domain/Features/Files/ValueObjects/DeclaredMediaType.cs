using AppTemplate.Domain.Core.Common.Exceptions;

namespace AppTemplate.Domain.Features.Files.ValueObjects;

/// <summary>
/// The media type a client said its file would be: a validated claim about nothing. It is safe for
/// showing the user their own claim and for refusing a type the application does not accept;
/// anything treating it as a fact about content — serving without <c>X-Content-Type-Options:
/// nosniff</c>, choosing a decoder — is a vulnerability.
/// </summary>
public sealed record DeclaredMediaType
{
    /// <summary>IANA caps a type name and a subtype name at 127 characters each.</summary>
    public const int MaxTokenLength = 127;

    /// <summary>Both tokens at their maximum, plus the separating slash.</summary>
    public const int MaxLength = (MaxTokenLength * 2) + 1;

    private DeclaredMediaType(string type, string subtype)
    {
        Type = type;
        Subtype = subtype;
        Value = $"{type}/{subtype}";
    }

    /// <summary>The full <c>type/subtype</c>, lower-cased.</summary>
    public string Value { get; }

    /// <summary>The top-level type, such as <c>image</c>.</summary>
    public string Type { get; }

    public string Subtype { get; }

    /// <exception cref="DomainException">The value is not a well-formed <c>type/subtype</c>.</exception>
    public static DeclaredMediaType Create(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new DomainException("A media type cannot be empty.");
        }

        // Case-insensitive per RFC 9110, so "IMAGE/PNG" and "image/png" are one value.
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

        // Parameters are refused rather than stripped; the space and ';' fail the token test.
        if (!IsToken(type) || !IsToken(subtype))
        {
            throw new DomainException("A media type may only contain token characters.");
        }

        // '*' is a token character, so it needs its own rule: a wildcard says what a client accepts.
        if (type == "*" || subtype == "*")
        {
            throw new DomainException("A media type cannot be a wildcard.");
        }

        return new DeclaredMediaType(type, subtype);
    }

    public override string ToString() => Value;

    /// <summary>RFC 9110's <c>tchar</c>, minus the upper-case letters normalisation has removed.</summary>
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

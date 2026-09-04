using AppTemplate.Domain.Common.Exceptions;

namespace AppTemplate.Domain.Features.Files.ValueObjects;

/// <summary>
/// The media type a client said its file would be.
/// <para>
/// <b>This validates the shape of the claim and nothing about the bytes.</b> It cannot: the bytes
/// are deposited straight onto the object store and this aggregate never holds one of them. A file
/// whose declared type is <c>image/png</c> may perfectly well be an HTML document, a ZIP archive or
/// an executable, and every value produced here is exactly as trustworthy as the client that sent
/// it — which is to say, not at all.
/// </para>
/// <para>
/// Deciding what a file <em>is</em> means reading its content, and that belongs to whatever handles
/// the bytes after the deposit. Anything that treats this value as a fact about content — serving it
/// back as a <c>Content-Type</c> without <c>X-Content-Type-Options: nosniff</c>, choosing a decoder
/// from it, or deciding a file is safe because it claims to be an image — is a vulnerability, not a
/// shortcut. It is safe for exactly two things: showing the user what they said, and refusing a
/// deposit whose declared type is not one this application accepts at all.
/// </para>
/// </summary>
public sealed record DeclaredMediaType
{
    /// <summary>
    /// IANA caps a registered type name and a registered subtype name at 127 characters each, so a
    /// longer token is not a media type this or any other system will resolve.
    /// </summary>
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

    /// <summary>The top-level type — <c>image</c>, <c>audio</c>, <c>application</c>.</summary>
    public string Type { get; }

    public string Subtype { get; }

    /// <exception cref="DomainException">The value is not a well-formed <c>type/subtype</c>.</exception>
    public static DeclaredMediaType Create(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new DomainException("A media type cannot be empty.");
        }

        // Both tokens are case-insensitive per RFC 9110, so normalising is what makes "IMAGE/PNG"
        // and "image/png" one value rather than two rows the same query would have to match twice.
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

        // Parameters are refused rather than stripped. "image/png; charset=utf-8" is a valid header
        // value but not a valid answer to "what is this file", and a parser that discards what it
        // does not understand is how two components end up disagreeing about the same string. The
        // caller is told to send the bare type; the space and the ';' both fail the token test below.
        if (!IsToken(type) || !IsToken(subtype))
        {
            throw new DomainException("A media type may only contain token characters.");
        }

        // '*' is a valid token character, so this has to be its own rule. A wildcard is what an
        // Accept header carries — a statement about what a client will take — and it is never a
        // statement about what one particular file is.
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

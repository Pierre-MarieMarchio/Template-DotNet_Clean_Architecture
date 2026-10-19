using AppTemplate.Domain.Core.Common.Exceptions;

namespace AppTemplate.Domain.Features.Files.Model.ValueObjects;

/// <summary>
/// A SHA-256 digest of a file's content, as 64 hexadecimal characters. The algorithm is in the type name so a digest
/// of another algorithm, which could never equal the store's, is a compile-time question.
/// </summary>
public sealed record Sha256Checksum
{
    /// <summary>256 bits at four bits per hexadecimal character; the only length.</summary>
    public const int Length = 64;

    private Sha256Checksum(string value) => Value = value;

    public string Value { get; }

    /// <exception cref="DomainException">The value is not 64 hexadecimal characters.</exception>
    public static Sha256Checksum Create(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new DomainException("A checksum cannot be empty.");
        }

        // Normalised rather than required: the client and the store compute the two digests and need
        // not agree on casing.
        string normalised = value.Trim().ToLowerInvariant();

        if (normalised.Length != Length)
        {
            throw new DomainException($"A SHA-256 checksum is exactly {Length} hexadecimal characters.");
        }

        foreach (char character in normalised)
        {
            if (!char.IsAsciiHexDigitLower(character))
            {
                throw new DomainException("A SHA-256 checksum may only contain hexadecimal characters.");
            }
        }

        return new Sha256Checksum(normalised);
    }

    public override string ToString() => Value;
}

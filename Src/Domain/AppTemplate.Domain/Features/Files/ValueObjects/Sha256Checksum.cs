using AppTemplate.Domain.Common.Exceptions;

namespace AppTemplate.Domain.Features.Files.ValueObjects;

/// <summary>
/// A SHA-256 digest of a file's content, as 64 hexadecimal characters.
/// <para>
/// The algorithm is in the name on purpose. This value is compared for equality against what the
/// object store reports for the deposited object, and a comparison between digests of two different
/// algorithms is not a mismatch to investigate — it is a value that can never be equal, which would
/// present as every single file failing confirmation for a reason no message explains. A type that
/// names its algorithm makes that a compile-time question instead.
/// </para>
/// </summary>
public sealed record Sha256Checksum
{
    /// <summary>256 bits, at four bits per hexadecimal character. Not a limit — the only length.</summary>
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

        // Case is normalised rather than required, because the two ends of the comparison are not
        // under the same control: the client computes one digest and the object store reports the
        // other, and the two tools need not agree on casing. Comparing these values rather than the
        // raw strings is what makes the confirmation check correct whichever casing arrives.
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

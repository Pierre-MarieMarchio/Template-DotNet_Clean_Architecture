using AppTemplate.Domain.Core.Common.Exceptions;

namespace AppTemplate.Domain.Features.Files.Model.ValueObjects;

public sealed record Sha256Checksum
{
    public const int Length = 64;

    private Sha256Checksum(string value) => Value = value;

    public string Value { get; }

    public static Sha256Checksum Create(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new DomainException("A checksum cannot be empty.");
        }

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

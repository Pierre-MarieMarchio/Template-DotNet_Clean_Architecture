using System.Buffers;
using System.Globalization;
using System.Security.Cryptography;
using AppTemplate.Domain.Core.Common.Exceptions;

namespace AppTemplate.Domain.Features.Files.ValueObjects;

/// <summary>
/// The name the bytes of a <see cref="Entities.StoredFile"/> are filed under in the object store:
/// data, not a formula — minted once by <see cref="New"/>, persisted beside the file and never
/// recomputed from its id (see <c>docs/DECISIONS.md</c>).
/// </summary>
public sealed record ObjectKey
{
    /// <summary>
    /// An opaque, reserved partition: the day tenancy arrives its identifier goes in this slot, so
    /// a prefix-scoped policy can be written per tenant without moving an existing object.
    /// </summary>
    public const string UnpartitionedPrefix = "t0";

    /// <summary>128 bits of entropy, sized against enumeration through a mis-scoped bucket policy rather than collision.</summary>
    public const int NameLength = 32;

    /// <summary>Half the store's 1024-byte cap, leaving room for a future tenant segment.</summary>
    public const int MaxLength = 512;

    // Narrower than the store accepts: a key is pasted into a signed URL path, so every character a URL parser, shell
    // or filesystem treats specially is a place for components to disagree. Lower case only, because case-insensitive
    // tooling would see two keys as one.
    private static readonly SearchValues<char> _allowedCharacters =
        SearchValues.Create("abcdefghijklmnopqrstuvwxyz0123456789-_./");

    private ObjectKey(string value) => Value = value;

    public string Value { get; }

    /// <summary>Mints a key for a file about to be registered.</summary>
    /// <param name="registeredAt">Must be the instant the aggregate stores as its registration instant, or the sweep
    /// would look under the wrong <see cref="TimeSegmentFor"/> prefix.</param>
    public static ObjectKey New(DateTimeOffset registeredAt) =>
        // Through Create, so minting and loading cannot disagree about what is valid.
        Create(
            $"{UnpartitionedPrefix}/{TimeSegmentFor(registeredAt)}/" +
            RandomNumberGenerator.GetHexString(NameLength, lowercase: true));

    /// <summary>
    /// The UTC month slice a key minted at <paramref name="instant"/> belongs to; one method for
    /// mint and sweep, in UTC, so the two cannot disagree near a month boundary (see
    /// <c>docs/DECISIONS.md</c>).
    /// </summary>
    public static string TimeSegmentFor(DateTimeOffset instant) =>
        instant.UtcDateTime.ToString("yyyyMM", CultureInfo.InvariantCulture);

    /// <summary>Parses an existing key, from a stored row or a caller naming an object.</summary>
    /// <exception cref="DomainException">The value is not a well-formed key.</exception>
    public static ObjectKey Create(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new DomainException("An object key cannot be empty.");
        }

        // Not trimmed, unlike the other text value objects: " a/b" and "a/b" are different objects.
        if (value.Length > MaxLength)
        {
            throw new DomainException($"An object key cannot exceed {MaxLength} characters.");
        }

        if (value.AsSpan().ContainsAnyExcept(_allowedCharacters))
        {
            throw new DomainException("An object key may only contain lower-case letters, digits, '-', '_', '.' and '/'.");
        }

        string[] segments = value.Split('/');

        // Two, although New mints three: a parser insisting on the current shape could not load keys
        // minted under another scheme.
        if (segments.Length < 2)
        {
            throw new DomainException("An object key must have a prefix segment and a name segment.");
        }

        foreach (string segment in segments)
        {
            // Empty segments (stray slashes) are normalised by some store clients and not others;
            // '.' and '..' are traversal that proxies and signers do not all resolve alike.
            if (segment.Length == 0 || segment is "." or "..")
            {
                throw new DomainException("An object key cannot contain an empty, '.' or '..' segment.");
            }
        }

        return new ObjectKey(value);
    }

    public override string ToString() => Value;
}

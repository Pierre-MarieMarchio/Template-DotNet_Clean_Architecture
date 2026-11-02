using System.Buffers;
using System.Globalization;
using System.Security.Cryptography;
using AppTemplate.Domain.Core.Common.Exceptions;

namespace AppTemplate.Domain.Features.Files.Model.ValueObjects;

public sealed record ObjectKey
{
    public const string UnpartitionedPrefix = "t0";

    public const int NameLength = 32;

    public const int MaxLength = 512;

    private static readonly SearchValues<char> _allowedCharacters =
        SearchValues.Create("abcdefghijklmnopqrstuvwxyz0123456789-_./");

    private ObjectKey(string value) => Value = value;

    public string Value { get; }

    public static ObjectKey New(DateTimeOffset registeredAt) =>
        Create(
            $"{UnpartitionedPrefix}/{TimeSegmentFor(registeredAt)}/" +
            RandomNumberGenerator.GetHexString(NameLength, lowercase: true));

    public static string TimeSegmentFor(DateTimeOffset instant) =>
        instant.UtcDateTime.ToString("yyyyMM", CultureInfo.InvariantCulture);

    public static ObjectKey Create(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new DomainException("An object key cannot be empty.");
        }

        if (value.Length > MaxLength)
        {
            throw new DomainException($"An object key cannot exceed {MaxLength} characters.");
        }

        if (value.AsSpan().ContainsAnyExcept(_allowedCharacters))
        {
            throw new DomainException("An object key may only contain lower-case letters, digits, '-', '_', '.' and '/'.");
        }

        string[] segments = value.Split('/');

        if (segments.Length < 2)
        {
            throw new DomainException("An object key must have a prefix segment and a name segment.");
        }

        foreach (string segment in segments)
        {
            if (segment.Length == 0 || segment is "." or "..")
            {
                throw new DomainException("An object key cannot contain an empty, '.' or '..' segment.");
            }
        }

        return new ObjectKey(value);
    }

    public override string ToString() => Value;
}

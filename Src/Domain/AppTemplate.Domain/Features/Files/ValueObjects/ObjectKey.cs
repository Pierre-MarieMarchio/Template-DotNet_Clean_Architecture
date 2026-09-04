using System.Buffers;
using System.Globalization;
using System.Security.Cryptography;
using AppTemplate.Domain.Common.Exceptions;

namespace AppTemplate.Domain.Features.Files.ValueObjects;

/// <summary>
/// The name the bytes of a <see cref="Entities.StoredFile"/> are filed under in the object store.
/// <para>
/// <b>It is data, not a formula.</b> A key is minted once by <see cref="New"/> and persisted beside
/// the file it belongs to; nothing anywhere recomputes it from the file's id. Two independent
/// reasons, and either one on its own would be enough:
/// </para>
/// <para>
/// First, a derived key makes the derivation part of the stored data's format. Changing the scheme
/// — adding a segment, changing a separator, partitioning differently — would then mean rewriting
/// the name of every object already written, which for an object store means copying the bytes.
/// A stored key makes every one of those changes apply to new files only, and old files keep
/// working because their name was never a function of anything.
/// </para>
/// <para>
/// Second, the id is a UUIDv7: it embeds a timestamp and is therefore guessable in sequence. If the
/// key were derived from it, anyone holding one key could enumerate its neighbours, and a bucket
/// policy that is one clause too generous stops being a mistake and becomes a directory listing.
/// The name segment is <see cref="NameLength"/> hexadecimal characters from a cryptographic
/// generator instead, so knowing one key says nothing about any other.
/// </para>
/// </summary>
public sealed record ObjectKey
{
    /// <summary>
    /// The first segment of every key, and the reason a key has segments at all. It is opaque and
    /// reserved: there is no tenancy in this repository today (no type names a tenant, and none is
    /// being added), but the day one arrives its identifier goes in this slot, and the key namespace
    /// is already partitioned — so a bucket policy or a prefix-scoped credential can be written per
    /// tenant without a single existing object moving. Objects written before that day keep this
    /// value for ever and stay addressable, which is only true because the key was stored.
    /// </summary>
    public const string UnpartitionedPrefix = "t0";

    /// <summary>
    /// 32 hexadecimal characters, so 128 bits of entropy. Sized against guessing rather than against
    /// collision: collision resistance would be satisfied by far fewer bits, but a key that leaks
    /// through a mis-scoped bucket policy must still be useless as a starting point for enumeration.
    /// </summary>
    public const int NameLength = 32;

    /// <summary>
    /// The object store caps a key at 1024 bytes. Half of that is the domain's ceiling: it leaves
    /// room for the tenant segment that does not exist yet without ever approaching the store's own
    /// limit, where the failure would arrive as a rejected write rather than as a refused value.
    /// </summary>
    public const int MaxLength = 512;

    /// <summary>
    /// Deliberately narrow, and narrower than the store would accept. A key is pasted into the path
    /// of a signed URL, so every character that means something to a URL parser, a shell, or a
    /// filesystem is one more place for two components to disagree about what the key was. Lower
    /// case only, because half the tooling that will ever touch these names is case-insensitive and
    /// two keys differing only in case would be one object to it.
    /// </summary>
    private static readonly SearchValues<char> _allowedCharacters =
        SearchValues.Create("abcdefghijklmnopqrstuvwxyz0123456789-_./");

    private ObjectKey(string value) => Value = value;

    public string Value { get; }

    /// <summary>Mints a key for a file that is about to be registered.</summary>
    /// <param name="registeredAt">The instant the file is being registered, which becomes the key's
    /// time segment. It must be the same value the aggregate stores as its registration instant, or
    /// the sweep described on <see cref="TimeSegmentFor"/> would look for a file's bytes under a
    /// prefix they are not in.</param>
    public static ObjectKey New(DateTimeOffset registeredAt) =>
        // Routed through Create rather than returning directly, so the minting path and the loading
        // path can never disagree about what a valid key is: a change to the rules below that this
        // no longer satisfies fails on the first file registered, not on the first file reloaded.
        Create(
            $"{UnpartitionedPrefix}/{TimeSegmentFor(registeredAt)}/" +
            RandomNumberGenerator.GetHexString(NameLength, lowercase: true));

    /// <summary>
    /// The time slice a key minted at <paramref name="instant"/> belongs to.
    /// <para>
    /// This is what keeps the orphan sweep bounded. Reclaiming the bytes of deleted files is done by
    /// difference — list the store, subtract the keys the live rows name, delete what is left — and
    /// with a flat namespace the rows to check for any one page of keys could be anywhere in the
    /// table. The segment bounds that: an object under a given slice can only have been minted by a
    /// row registered in that slice, because <c>Register</c> mints from the very instant it stores.
    /// </para>
    /// <para>
    /// <b>What it does not license is visiting only recent slices.</b> A file registered two years
    /// ago and deleted today has its bytes under a two-year-old prefix, and a sweep that only looked
    /// at the current one would never reclaim them — the sweep would stop being the guarantee and
    /// become a heuristic. Every slice has to be covered; the segment makes each pass cheap and
    /// ordered, not the coverage narrower.
    /// </para>
    /// <para>
    /// <b>UTC, deliberately.</b> The sweep computes this from a row's stored registration instant and
    /// the mint computes it from the same instant, so the two must agree for every caller. A local
    /// interpretation would put a file registered near midnight in one slice on one host and another
    /// slice on another, and the sweep would find its bytes unreferenced and delete a live file. Both
    /// sides call this method rather than formatting a date themselves, so there is one answer.
    /// </para>
    /// </summary>
    public static string TimeSegmentFor(DateTimeOffset instant) =>
        instant.UtcDateTime.ToString("yyyyMM", CultureInfo.InvariantCulture);

    /// <summary>
    /// Parses a key that already exists — from a stored row, or from a caller naming an object.
    /// </summary>
    /// <exception cref="DomainException">The value is not a well-formed key.</exception>
    public static ObjectKey Create(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new DomainException("An object key cannot be empty.");
        }

        // Not trimmed, unlike every other text value object here. Whitespace is significant to the
        // store: " a/b" and "a/b" name two different objects, so silently trimming would hand back a
        // key addressing bytes the caller did not ask for. Refusing is the only safe normalisation.
        if (value.Length > MaxLength)
        {
            throw new DomainException($"An object key cannot exceed {MaxLength} characters.");
        }

        if (value.AsSpan().ContainsAnyExcept(_allowedCharacters))
        {
            throw new DomainException("An object key may only contain lower-case letters, digits, '-', '_', '.' and '/'.");
        }

        string[] segments = value.Split('/');

        // Two, not three, although New mints three. Parsing is deliberately looser than minting: the
        // whole reason a key is stored rather than derived is that the scheme may change, and a
        // parser that insisted on today's shape would refuse to load the keys of yesterday's files —
        // which is exactly the "changing the scheme means moving the bytes" cost being avoided.
        if (segments.Length < 2)
        {
            throw new DomainException("An object key must have a prefix segment and a name segment.");
        }

        foreach (string segment in segments)
        {
            // An empty segment is a leading slash, a trailing slash or a doubled one, each of which
            // some store clients normalise away and others do not. A '.' or '..' segment is path
            // traversal: the store resolves keys literally, but the proxies, signers and CLI tools
            // in front of it do not all agree, and a key escaping its own prefix would escape the
            // tenant partition the prefix exists to enforce.
            if (segment.Length == 0 || segment is "." or "..")
            {
                throw new DomainException("An object key cannot contain an empty, '.' or '..' segment.");
            }
        }

        return new ObjectKey(value);
    }

    public override string ToString() => Value;
}

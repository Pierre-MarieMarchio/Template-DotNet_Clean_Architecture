using System.Buffers.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AppTemplate.Application.Common.Collections;

/// <summary>
/// A keyset bookmark: the sort field and direction it was minted under, the value the sort key held
/// on the last row of the page it was minted from, and that row's id as a tiebreaker.
/// </summary>
/// <remarks>
/// <b>Opaque, but not authenticated.</b> Nothing here is signed, and it does not need to be: a
/// cursor carries only values the caller was already served on a row they were already allowed to
/// see, and the query that resumes from it still filters by owner regardless of what the cursor
/// claims. Tampering with it can only change which of the caller's own rows show up next, or produce
/// a cursor this type refuses to decode; it cannot widen what the caller may read.
/// </remarks>
public sealed record Cursor
{
    /// <summary>Comfortably above any real encoded cursor, and small enough to reject abuse outright.</summary>
    public const int MaxEncodedLength = 512;

    /// <summary>
    /// A cursor's key is a value read out of one row; nothing legitimate produces one longer than
    /// this. Enforced here, in the layer that owns <c>cursor.invalid</c>, rather than in the
    /// persistence layer that only finds out a key is unreasonable once it fails to parse.
    /// </summary>
    private const int _maxKeyLength = 128;

    private static readonly JsonSerializerOptions _jsonOptions = new() { PropertyNamingPolicy = null };

    private Cursor(string field, SortDirection direction, string key, Guid id)
    {
        Field = field;
        Direction = direction;
        Key = key;
        Id = id;
    }

    /// <summary>The whitelist's canonical spelling of the field this cursor was minted under.</summary>
    public string Field { get; }

    public SortDirection Direction { get; }

    /// <summary>The sort field's value on the row the cursor was minted from, in its wire form.</summary>
    public string Key { get; }

    /// <summary>The tiebreaker: that row's id.</summary>
    public Guid Id { get; }

    /// <summary>Mints the cursor a caller sends back to resume after the given row.</summary>
    public static Cursor After(SortTerm term, string key, Guid id)
    {
        ArgumentNullException.ThrowIfNull(term);

        if (string.IsNullOrEmpty(key))
        {
            throw new ArgumentException("A cursor's key cannot be null or empty.", nameof(key));
        }

        return new Cursor(term.Field, term.Direction, key, id);
    }

    /// <summary>Base64Url (no padding) of a small deterministic JSON object.</summary>
    public string Encode()
    {
        var payload = new CursorPayload(Field, Direction == SortDirection.Ascending ? "asc" : "desc", Key, Id.ToString());
        byte[] json = JsonSerializer.SerializeToUtf8Bytes(payload, _jsonOptions);

        return Base64Url.EncodeToString(json);
    }

    /// <summary>
    /// Every failure here is <c>cursor.invalid</c>, and none of them echo <paramref name="raw"/> back
    /// in the message: it is caller input that ends up in logs, not a value this API should promise
    /// to repeat.
    /// </summary>
    internal static Result<Cursor> Decode(string raw, ICollectionPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(raw);
        ArgumentNullException.ThrowIfNull(policy);

        if (raw.Length > MaxEncodedLength)
        {
            return Result.Failure<Cursor>(CollectionErrors.InvalidCursor(
                $"The cursor exceeds the maximum length of {MaxEncodedLength} characters."));
        }

        byte[] json;

        try
        {
            json = Base64Url.DecodeFromChars(raw);
        }
        catch (FormatException)
        {
            return Result.Failure<Cursor>(CollectionErrors.InvalidCursor("The cursor is not valid Base64Url."));
        }

        CursorPayload? payload;

        try
        {
            payload = JsonSerializer.Deserialize<CursorPayload>(json, _jsonOptions);
        }
        catch (JsonException)
        {
            return Result.Failure<Cursor>(CollectionErrors.InvalidCursor("The cursor's payload is not valid JSON."));
        }

        if (payload is null
            || string.IsNullOrWhiteSpace(payload.F)
            || string.IsNullOrWhiteSpace(payload.D)
            || string.IsNullOrEmpty(payload.K)
            || string.IsNullOrWhiteSpace(payload.I))
        {
            return Result.Failure<Cursor>(CollectionErrors.InvalidCursor("The cursor is missing a required field."));
        }

        if (payload.K.Length > _maxKeyLength)
        {
            return Result.Failure<Cursor>(CollectionErrors.InvalidCursor(
                $"The cursor's key exceeds the maximum length of {_maxKeyLength} characters."));
        }

        SortDirection direction;

        if (string.Equals(payload.D, "asc", StringComparison.OrdinalIgnoreCase))
        {
            direction = SortDirection.Ascending;
        }
        else if (string.Equals(payload.D, "desc", StringComparison.OrdinalIgnoreCase))
        {
            direction = SortDirection.Descending;
        }
        else
        {
            return Result.Failure<Cursor>(CollectionErrors.InvalidCursor(
                "The cursor's sort direction is not recognised."));
        }

        if (!Guid.TryParse(payload.I, out Guid id))
        {
            return Result.Failure<Cursor>(CollectionErrors.InvalidCursor("The cursor's id is not a valid GUID."));
        }

        var field = policy.SortableFields.FirstOrDefault(
            candidate => string.Equals(candidate.Name, payload.F, StringComparison.OrdinalIgnoreCase));

        if (field is null)
        {
            return Result.Failure<Cursor>(CollectionErrors.InvalidCursor(
                $"'{payload.F}' is not a sortable field."));
        }

        if (!field.SupportsKeyset)
        {
            string keysetFields = string.Join(
                ", ",
                policy.SortableFields.Where(candidate => candidate.SupportsKeyset).Select(candidate => candidate.Name));

            return Result.Failure<Cursor>(CollectionErrors.InvalidCursor(
                $"'{field.Name}' cannot be used with paging=cursor. Fields that can: {keysetFields}."));
        }

        return Result.Success(new Cursor(field.Name, direction, payload.K, id));
    }

    private sealed record CursorPayload(
        [property: JsonPropertyName("f")] string F,
        [property: JsonPropertyName("d")] string D,
        [property: JsonPropertyName("k")] string K,
        [property: JsonPropertyName("i")] string I);
}

using System.Buffers.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using AppTemplate.Application.Core.Common.Policies;
using AppTemplate.Application.Core.Common.Results;

namespace AppTemplate.Application.Core.Common.Collections;

public sealed record Cursor
{
    public const int MaxEncodedLength = 512;

    private const int _maxKeyLength = 128;

    private static readonly JsonSerializerOptions _jsonOptions = new() { PropertyNamingPolicy = null };

    private Cursor(string field, SortDirection direction, string key, Guid id)
    {
        Field = field;
        Direction = direction;
        Key = key;
        Id = id;
    }

    public string Field { get; }

    public SortDirection Direction { get; }

    public string Key { get; }

    public Guid Id { get; }

    public static Cursor After(SortTerm term, string key, Guid id)
    {
        ArgumentNullException.ThrowIfNull(term);

        if (string.IsNullOrEmpty(key))
        {
            throw new ArgumentException("A cursor's key cannot be null or empty.", nameof(key));
        }

        return new Cursor(term.Field, term.Direction, key, id);
    }

    public string Encode()
    {
        var payload = new CursorPayload(Field, Direction == SortDirection.Ascending ? "asc" : "desc", Key, Id.ToString());
        byte[] json = JsonSerializer.SerializeToUtf8Bytes(payload, _jsonOptions);

        return Base64Url.EncodeToString(json);
    }

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

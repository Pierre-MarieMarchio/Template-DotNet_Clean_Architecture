using System.Globalization;
using AppTemplate.Application.Core.Common.Results;

namespace AppTemplate.Application.Core.Common.Collections;

/// <summary>Checks a decoded cursor's key against the CLR type of the field it was minted under, for the fields a feature says hold a date.</summary>
/// <remarks><see cref="Cursor.Decode"/> checks a cursor's shape only; whether the key parses as
/// the field's own type is the feature's to say, which is why it names its date fields here. Nothing
/// signs a cursor's payload, so a caller can hand back one with the key edited — and the check here,
/// before the request reaches the port, is what keeps a tampered cursor where every other malformed
/// request lands: a 400 with <c>cursor.invalid</c>, not a 500 out of the keyset predicate.</remarks>
internal static class CursorKeys
{
    /// <param name="cursor">A cursor that has already cleared <see cref="Cursor.Decode"/>.</param>
    /// <param name="dateFields">
    /// The feature's own fields whose values are instants. A collection with none of them passes
    /// none, and every key it can carry is a string that needs no parsing.
    /// </param>
    public static Result<Cursor> Validate(Cursor cursor, params string[] dateFields)
    {
        ArgumentNullException.ThrowIfNull(cursor);

        if (dateFields.Contains(cursor.Field, StringComparer.Ordinal)
            && !DateTimeOffset.TryParse(cursor.Key, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out _))
        {
            return Result.Failure<Cursor>(
                CollectionErrors.InvalidCursor("The cursor's key is not a valid date/time for its field."));
        }

        return Result.Success(cursor);
    }
}

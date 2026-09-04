using System.Globalization;
using AppTemplate.Application.Common.Results;

namespace AppTemplate.Application.Common.Collections;

/// <summary>
/// Checks a decoded cursor's key against the CLR type of the field it was minted under, for the
/// fields a feature says hold a date.
/// </summary>
/// <remarks>
/// <see cref="Cursor.Decode"/> only checks a cursor's shape: a whitelisted field, a recognised
/// direction, a non-blank key short enough to be real. It cannot check whether the key parses as
/// that field's own type, because the generic machinery in this namespace does not know which of a
/// feature's fields are dates and which are bare strings — only the feature does, which is why it
/// names them here.
/// <para>
/// Nothing signs a cursor's payload, so a caller can hand back one with the key edited. Without this
/// check, an unparseable date key would only be discovered by the persistence layer's keyset
/// predicate, whose one recourse against a value that should have been validated already is to
/// throw — which the global exception handler turns into a 500. Running the check here, before the
/// request ever reaches the port, keeps a tampered cursor exactly where every other malformed
/// request already lands: a 400 with <c>cursor.invalid</c>.
/// </para>
/// </remarks>
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

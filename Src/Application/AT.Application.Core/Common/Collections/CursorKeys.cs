using System.Globalization;
using AppTemplate.Application.Core.Common.Results;

namespace AppTemplate.Application.Core.Common.Collections;

internal static class CursorKeys
{
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

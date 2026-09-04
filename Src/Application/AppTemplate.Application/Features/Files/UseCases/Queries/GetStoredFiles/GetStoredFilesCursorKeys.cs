using System.Globalization;
using AppTemplate.Application.Common.Collections;
using AppTemplate.Application.Common.Results;
using AppTemplate.Application.Features.Files.Policies;

namespace AppTemplate.Application.Features.Files.UseCases.Queries.GetStoredFiles;

/// <summary>
/// <see cref="Cursor.Decode"/> only checks a cursor's shape: a whitelisted field, a recognised
/// direction, a non-blank key short enough to be real. It cannot check whether the key parses as
/// that field's own CLR type, because the generic machinery in <c>Common/Collections/</c> does not
/// know that <see cref="StoredFileCollectionPolicy.RegisteredAtField"/> is a date and
/// <see cref="StoredFileCollectionPolicy.NameField"/> is a bare string — only this feature does.
/// </summary>
/// <remarks>
/// Nothing signs a cursor's payload, so a caller can hand one back with the key edited. Left
/// unchecked, an unparseable date key would only be discovered by the persistence layer's keyset
/// predicate, whose one recourse against a value that should already have been validated is to
/// throw — a 500 for what is a malformed request. Refused here, it lands where every other broken
/// rule does: a 400 with <c>cursor.invalid</c>.
/// </remarks>
internal static class GetStoredFilesCursorKeys
{
    public static Result<Cursor> Validate(Cursor cursor)
    {
        ArgumentNullException.ThrowIfNull(cursor);

        if (cursor.Field == StoredFileCollectionPolicy.RegisteredAtField
            && !DateTimeOffset.TryParse(cursor.Key, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out _))
        {
            return Result.Failure<Cursor>(
                CollectionErrors.InvalidCursor("The cursor's key is not a valid date/time for its field."));
        }

        return Result.Success(cursor);
    }
}

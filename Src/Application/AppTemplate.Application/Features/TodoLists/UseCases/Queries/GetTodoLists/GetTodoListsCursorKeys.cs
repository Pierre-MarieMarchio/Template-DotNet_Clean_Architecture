using System.Globalization;
using AppTemplate.Application.Common.Collections;
using AppTemplate.Application.Common.Results;
using AppTemplate.Application.Features.TodoLists.Policies;

namespace AppTemplate.Application.Features.TodoLists.UseCases.Queries.GetTodoLists;

/// <summary>
/// <see cref="Cursor.Decode"/> only checks a cursor's shape: a whitelisted field, a recognised
/// direction, a non-blank key short enough to be real. It cannot check whether the key parses as
/// that field's own CLR type, because the generic machinery in <c>Common/Collections/</c> does not
/// know that <see cref="TodoListCollectionPolicy.CreatedAtField"/> is a date and
/// <see cref="TodoListCollectionPolicy.NameField"/> is a bare string — only this feature does.
/// </summary>
/// <remarks>
/// Nothing signs a cursor's payload, so a caller can hand back one with the key edited. Without this
/// check, an unparseable date key would only be discovered by the persistence layer's keyset
/// predicate, whose one recourse against a value that should have been validated already is to
/// throw — which the global exception handler turns into a 500. Running the same check here, before
/// the request ever reaches the port, keeps a tampered cursor exactly where every other malformed
/// request already lands: a 400 with <c>cursor.invalid</c>.
/// </remarks>
internal static class GetTodoListsCursorKeys
{
    public static Result<Cursor> Validate(Cursor cursor)
    {
        ArgumentNullException.ThrowIfNull(cursor);

        if (cursor.Field == TodoListCollectionPolicy.CreatedAtField
            && !DateTimeOffset.TryParse(cursor.Key, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out _))
        {
            return Result.Failure<Cursor>(
                CollectionErrors.InvalidCursor("The cursor's key is not a valid date/time for its field."));
        }

        return Result.Success(cursor);
    }
}

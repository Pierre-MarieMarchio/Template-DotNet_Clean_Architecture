using AppTemplate.Application.Common;
using AppTemplate.Application.Common.Collections;
using AppTemplate.Application.Features.TodoLists.UseCases.Queries;

namespace AppTemplate.Application.Features.TodoLists.Collections;

/// <summary>
/// Turns a <see cref="GetTodoListsQuery"/>'s raw strings into a <see cref="TodoListPageRequest"/>,
/// applying <see cref="TodoListCollectionPolicy.Instance"/>'s whitelist along the way. Kept apart
/// from the use case because none of this is application logic — it is query-string translation,
/// and the next paginated collection this feature grows will need its own copy of exactly this
/// shape rather than of the use case around it.
/// </summary>
public static class TodoListRequestBinder
{
    public static Result<TodoListPageRequest> Bind(GetTodoListsQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);

        var policy = TodoListCollectionPolicy.Instance;

        var modeResult = PageRequest.ParseMode(query.Paging);

        if (modeResult.IsFailure)
        {
            return modeResult.To<TodoListPageRequest>();
        }

        var mode = modeResult.Value;

        var sortResult = SortOrder.Parse(query.Sort, policy);

        if (sortResult.IsFailure)
        {
            return sortResult.To<TodoListPageRequest>();
        }

        var sort = sortResult.Value;

        // A keyset comparison over more than one key plus the id tiebreaker is a row comparison this
        // template does not implement. Checked here rather than left to PageRequest.Create, and
        // unconditionally rather than only once a cursor is actually sent, so a caller cannot be let
        // through on page 1 only to be refused on page 2.
        if (mode == PagingMode.Cursor && sort.Terms.Count > 1)
        {
            return Result.Failure<TodoListPageRequest>(
                CollectionErrors.InvalidCursor("Cursor paging supports a single sort field."));
        }

        var filterResult = TodoListFilter.Create(query.Search, query.CreatedAfter, query.CreatedBefore);

        if (filterResult.IsFailure)
        {
            return filterResult.To<TodoListPageRequest>();
        }

        var filter = filterResult.Value;

        Cursor? cursor = null;

        // Decoded whenever a cursor was sent, regardless of mode: a cursor sent with paging=offset
        // must still fail — through PageRequest.Create, which is the one place that already knows
        // the two are alternatives — rather than being silently ignored because the mode did not
        // match.
        if (!string.IsNullOrWhiteSpace(query.Cursor))
        {
            var cursorResult = Cursor.Decode(query.Cursor, policy);

            if (cursorResult.IsFailure)
            {
                return cursorResult.To<TodoListPageRequest>();
            }

            var keyResult = TodoListCursorKeys.Validate(cursorResult.Value);

            if (keyResult.IsFailure)
            {
                return keyResult.To<TodoListPageRequest>();
            }

            cursor = keyResult.Value;

            // The cursor names the order it was minted under, and the read side compares the cursor's
            // key using the *request's* sort term — so the two disagreeing is not a difference of
            // opinion to resolve, it is a comparison between a value and a column that do not match.
            // Left unchecked, resuming a name-ordered cursor under sort=createdAt would parse a list's
            // name as a date, and the only recourse the persistence layer has at that point is to
            // throw, which is a 500 for what is really a malformed request. Refused here, where it is
            // a 400 like every other broken rule.
            //
            // Deliberately not resolved by preferring one over the other: silently ignoring `sort`
            // because a cursor was sent would serve a page in an order the caller did not ask for,
            // and silently re-minting the cursor would skip or repeat rows.
            if (mode == PagingMode.Cursor
                && (!string.Equals(cursor.Field, sort.Terms[0].Field, StringComparison.Ordinal)
                    || cursor.Direction != sort.Terms[0].Direction))
            {
                return Result.Failure<TodoListPageRequest>(CollectionErrors.InvalidCursor(
                    "This cursor was minted under a different sort order. Send the same 'sort' the "
                    + "cursor came from, or start again without a cursor."));
            }
        }

        var pagingResult = PageRequest.Create(mode, query.Page, query.PageSize, cursor, policy);

        if (pagingResult.IsFailure)
        {
            return pagingResult.To<TodoListPageRequest>();
        }

        return TodoListPageRequest.Of(pagingResult.Value, sort, filter);
    }
}

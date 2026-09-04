using AppTemplate.Application.Common.Collections;
using AppTemplate.Application.Common.Results;
using AppTemplate.Application.Features.Files.Policies;
using AppTemplate.Application.Features.Files.Ports.StoredFileQueries;

namespace AppTemplate.Application.Features.Files.UseCases.Queries.GetStoredFiles;

/// <summary>
/// Turns a <see cref="GetStoredFilesQuery"/>'s raw strings into a <see cref="StoredFilePageRequest"/>,
/// applying <see cref="StoredFileCollectionPolicy.Instance"/>'s whitelist along the way. Kept apart
/// from the use case because none of this is application logic — it is query-string translation.
/// </summary>
public static class GetStoredFilesRequestBinder
{
    public static Result<StoredFilePageRequest> Bind(GetStoredFilesQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);

        var policy = StoredFileCollectionPolicy.Instance;

        var modeResult = PageRequest.ParseMode(query.Paging);

        if (modeResult.IsFailure)
        {
            return modeResult.To<StoredFilePageRequest>();
        }

        var mode = modeResult.Value;

        var sortResult = SortOrder.Parse(query.Sort, policy);

        if (sortResult.IsFailure)
        {
            return sortResult.To<StoredFilePageRequest>();
        }

        var sort = sortResult.Value;

        // A keyset comparison over more than one key plus the id tiebreaker is a row comparison this
        // template does not implement. Refused unconditionally rather than only once a cursor is
        // actually sent, so a caller cannot be let through on page 1 and refused on page 2.
        if (mode == PagingMode.Cursor && sort.Terms.Count > 1)
        {
            return Result.Failure<StoredFilePageRequest>(
                CollectionErrors.InvalidCursor("Cursor paging supports a single sort field."));
        }

        var filterResult = StoredFileFilter.Create(query.Search, query.State);

        if (filterResult.IsFailure)
        {
            return filterResult.To<StoredFilePageRequest>();
        }

        var filter = filterResult.Value;

        Cursor? cursor = null;

        // Decoded whenever a cursor was sent, regardless of mode: one sent with paging=offset must
        // still fail — through PageRequest.Create, which already knows the two are alternatives —
        // rather than being silently ignored because the mode did not match.
        if (!string.IsNullOrWhiteSpace(query.Cursor))
        {
            var cursorResult = Cursor.Decode(query.Cursor, policy);

            if (cursorResult.IsFailure)
            {
                return cursorResult.To<StoredFilePageRequest>();
            }

            var keyResult = GetStoredFilesCursorKeys.Validate(cursorResult.Value);

            if (keyResult.IsFailure)
            {
                return keyResult.To<StoredFilePageRequest>();
            }

            cursor = keyResult.Value;

            // The cursor names the order it was minted under and the read side compares its key
            // using the request's sort term, so the two disagreeing is a comparison between a value
            // and a column that do not match — resuming a name-ordered cursor under
            // sort=registeredAt would parse a file name as a date. Neither side is preferred:
            // ignoring `sort` would serve an order nobody asked for, and re-minting the cursor would
            // skip or repeat rows.
            if (mode == PagingMode.Cursor
                && (!string.Equals(cursor.Field, sort.Terms[0].Field, StringComparison.Ordinal)
                    || cursor.Direction != sort.Terms[0].Direction))
            {
                return Result.Failure<StoredFilePageRequest>(CollectionErrors.InvalidCursor(
                    "This cursor was minted under a different sort order. Send the same 'sort' the "
                    + "cursor came from, or start again without a cursor."));
            }
        }

        var pagingResult = PageRequest.Create(mode, query.Page, query.PageSize, cursor, policy);

        if (pagingResult.IsFailure)
        {
            return pagingResult.To<StoredFilePageRequest>();
        }

        return StoredFilePageRequest.Of(pagingResult.Value, sort, filter);
    }
}

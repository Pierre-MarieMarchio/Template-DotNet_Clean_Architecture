using AppTemplate.Application.Core.Common.Policies;
using AppTemplate.Application.Core.Common.Results;

namespace AppTemplate.Application.Core.Common.Collections;

public sealed record CollectionOrder
{
    private readonly ICollectionPolicy _policy;

    private CollectionOrder(PagingMode mode, SortOrder sort, ICollectionPolicy policy)
    {
        Mode = mode;
        Sort = sort;
        _policy = policy;
    }

    public PagingMode Mode { get; }

    public SortOrder Sort { get; }

    public static Result<CollectionOrder> Parse(string? paging, string? sort, ICollectionPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(policy);

        var modeResult = PageRequest.ParseMode(paging);

        if (modeResult.IsFailure)
        {
            return modeResult.To<CollectionOrder>();
        }

        var mode = modeResult.Value;

        var sortResult = SortOrder.Parse(sort, policy);

        if (sortResult.IsFailure)
        {
            return sortResult.To<CollectionOrder>();
        }

        var order = sortResult.Value;

        if (mode == PagingMode.Cursor && order.Terms.Count > 1)
        {
            return Result.Failure<CollectionOrder>(
                CollectionErrors.InvalidCursor("Cursor paging supports a single sort field."));
        }

        if (mode == PagingMode.Cursor)
        {
            var field = policy.SortableFields.First(
                candidate => string.Equals(candidate.Name, order.Terms[0].Field, StringComparison.Ordinal));

            if (!field.SupportsKeyset)
            {
                string keysetFields = string.Join(
                    ", ",
                    policy.SortableFields.Where(candidate => candidate.SupportsKeyset).Select(candidate => candidate.Name));

                return Result.Failure<CollectionOrder>(CollectionErrors.InvalidCursor(
                    $"'{field.Name}' cannot be used with paging=cursor. Fields that can: {keysetFields}."));
            }
        }

        return Result.Success(new CollectionOrder(mode, order, policy));
    }

    public Result<PageRequest> ToPageRequest(
        int? page,
        int? pageSize,
        string? cursor,
        params string[] dateKeyFields)
    {
        Cursor? decoded = null;

        if (!string.IsNullOrWhiteSpace(cursor))
        {
            var cursorResult = Cursor.Decode(cursor, _policy);

            if (cursorResult.IsFailure)
            {
                return cursorResult.To<PageRequest>();
            }

            var keyResult = CursorKeys.Validate(cursorResult.Value, dateKeyFields);

            if (keyResult.IsFailure)
            {
                return keyResult.To<PageRequest>();
            }

            decoded = keyResult.Value;

            if (Mode == PagingMode.Cursor
                && (!string.Equals(decoded.Field, Sort.Terms[0].Field, StringComparison.Ordinal)
                    || decoded.Direction != Sort.Terms[0].Direction))
            {
                return Result.Failure<PageRequest>(CollectionErrors.InvalidCursor(
                    "This cursor was minted under a different sort order. Send the same 'sort' the "
                    + "cursor came from, or start again without a cursor."));
            }
        }

        return PageRequest.Create(Mode, page, pageSize, decoded, _policy);
    }
}

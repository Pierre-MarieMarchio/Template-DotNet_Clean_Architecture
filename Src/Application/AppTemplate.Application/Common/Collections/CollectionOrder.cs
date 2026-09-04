using AppTemplate.Application.Common.Policies;
using AppTemplate.Application.Common.Results;

namespace AppTemplate.Application.Common.Collections;

/// <summary>
/// The paging mode and the sort order a caller asked for, parsed against a feature's own whitelist
/// and checked against each other — the half of a collection request that is the same whatever the
/// feature, and whatever that feature lets a caller filter by.
/// </summary>
/// <remarks>
/// Binding a collection request is deliberately two steps rather than one, because a feature's own
/// filter is built between them: <see cref="Parse"/> refuses what is wrong with the order,
/// the feature refuses what is wrong with its filter, and <see cref="ToPageRequest"/> then resolves
/// the cursor against the order it was minted under. A caller sending two mistakes at once is told
/// about the first of them in that order.
/// </remarks>
public sealed record CollectionOrder
{
    /// <summary>The policy this order was parsed against, so the cursor is resolved against the same one.</summary>
    private readonly ICollectionPolicy _policy;

    private CollectionOrder(PagingMode mode, SortOrder sort, ICollectionPolicy policy)
    {
        Mode = mode;
        Sort = sort;
        _policy = policy;
    }

    public PagingMode Mode { get; }

    /// <summary>Never empty: a blank <c>sort</c> parsed the policy's own default instead.</summary>
    public SortOrder Sort { get; }

    /// <summary>Parses the <c>paging</c> and <c>sort</c> query parameters together.</summary>
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

        // A keyset comparison over more than one key plus the id tiebreaker is a row comparison this
        // template does not implement. Checked here rather than left to PageRequest.Create, and
        // unconditionally rather than only once a cursor is actually sent, so a caller cannot be let
        // through on page 1 only to be refused on page 2.
        if (mode == PagingMode.Cursor && order.Terms.Count > 1)
        {
            return Result.Failure<CollectionOrder>(
                CollectionErrors.InvalidCursor("Cursor paging supports a single sort field."));
        }

        // The same reasoning, for the field rather than the count. Cursor.Decode already refuses a
        // cursor minted over an offset-only field, but the first cursor page carries no cursor to
        // refuse: without this, a caller ordering by a nullable column is served page 1 and the mint
        // of nextCursor then asks the read side for a key that field has no translation for, whose
        // only recourse is to throw. That is a 500 for what is a rule the caller broke, so it is
        // refused here with the code every other broken rule in this contract carries.
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

    /// <summary>
    /// Resolves the rest of the caller's paging — the page or the cursor — under this order.
    /// </summary>
    /// <param name="dateKeyFields">
    /// The feature's own fields whose values are instants, for <see cref="CursorKeys.Validate"/>.
    /// </param>
    public Result<PageRequest> ToPageRequest(
        int? page,
        int? pageSize,
        string? cursor,
        params string[] dateKeyFields)
    {
        Cursor? decoded = null;

        // Decoded whenever a cursor was sent, regardless of mode: a cursor sent with paging=offset
        // must still fail — through PageRequest.Create, which is the one place that already knows
        // the two are alternatives — rather than being silently ignored because the mode did not
        // match.
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

            // The cursor names the order it was minted under, and the read side compares the cursor's
            // key using the *request's* sort term — so the two disagreeing is not a difference of
            // opinion to resolve, it is a comparison between a value and a column that do not match.
            // Left unchecked, resuming a name-ordered cursor under a date-ordered sort would parse a
            // name as a date, and the only recourse the persistence layer has at that point is to
            // throw, which is a 500 for what is really a malformed request. Refused here, where it is
            // a 400 like every other broken rule.
            //
            // Deliberately not resolved by preferring one over the other: silently ignoring `sort`
            // because a cursor was sent would serve a page in an order the caller did not ask for,
            // and silently re-minting the cursor would skip or repeat rows.
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

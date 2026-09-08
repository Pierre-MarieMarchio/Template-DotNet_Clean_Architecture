
namespace AppTemplate.Application.Core.Common.Results;

/// <param name="Page">1-based page number that was served. Offset mode only.</param>
/// <param name="TotalCount">
/// Rows matching the query across all pages, not just this one. <c>null</c> in cursor mode: counting
/// the whole match set is a second scan of it, which is the cost keyset paging exists to avoid.
/// </param>
/// <param name="NextCursor">
/// Opaque token for the next page. <c>null</c> in offset mode, and <c>null</c> in cursor mode once
/// there is no further page.
/// </param>
public sealed record PagedResult<TItem>(
    IReadOnlyList<TItem> Items,
    int PageSize,
    int? Page,
    int? TotalCount,
    string? NextCursor)
{
    /// <summary><c>null</c> in cursor mode, where there is no total to divide.</summary>
    public int? TotalPages => TotalCount switch
    {
        null => null,
        { } total => PageSize <= 0 ? 0 : (int)Math.Ceiling(total / (double)PageSize),
    };

    /// <summary>
    /// Answered from whichever field the serving mode filled in, so a caller can ask without knowing
    /// which mode it got.
    /// </summary>
    public bool HasNextPage => NextCursor is not null || Page < TotalPages;
}

/// <summary>
/// Type-inferring entry points for <see cref="PagedResult{TItem}"/>, exactly as <see cref="Result"/>
/// carries the statics that would otherwise have to live on <see cref="Result{TValue}"/> and force
/// every caller to name the type argument (CA1000). One factory per paging mode keeps the two from
/// being assembled with the wrong fields — an offset page carrying a cursor, or a keyset page
/// carrying a total.
/// </summary>
public static class PagedResult
{
    /// <summary>An offset page: it knows the size of the whole match set, and mints no cursor.</summary>
    public static PagedResult<TItem> Offset<TItem>(
        IReadOnlyList<TItem> items,
        int page,
        int pageSize,
        int totalCount) =>
        new(items, pageSize, page, totalCount, null);

    /// <summary>
    /// A keyset page: it names the way on and no total, which is the trade this paging mode makes.
    /// </summary>
    /// <param name="nextCursor"><c>null</c> once there is no further page.</param>
    public static PagedResult<TItem> Keyset<TItem>(IReadOnlyList<TItem> items, int pageSize, string? nextCursor) =>
        new(items, pageSize, null, null, nextCursor);
}

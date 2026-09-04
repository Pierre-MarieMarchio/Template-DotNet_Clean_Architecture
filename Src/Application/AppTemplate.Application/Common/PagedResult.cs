namespace AppTemplate.Application.Common;

/// <param name="Page">1-based page number that was served.</param>
/// <param name="TotalCount">Rows matching the query across all pages, not just this one.</param>
public sealed record PagedResult<TItem>(
    IReadOnlyList<TItem> Items,
    int Page,
    int PageSize,
    int TotalCount)
{
    public int TotalPages => PageSize <= 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);

    public bool HasNextPage => Page < TotalPages;
}

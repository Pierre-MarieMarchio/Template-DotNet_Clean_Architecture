
namespace AppTemplate.Application.Core.Common.Results;

public sealed record PagedResult<TItem>(
    IReadOnlyList<TItem> Items,
    int PageSize,
    int? Page,
    int? TotalCount,
    string? NextCursor)
{
    public int? TotalPages => TotalCount switch
    {
        null => null,
        { } total => PageSize <= 0 ? 0 : (int)Math.Ceiling(total / (double)PageSize),
    };

    public bool HasNextPage => NextCursor is not null || Page < TotalPages;
}

public static class PagedResult
{
    public static PagedResult<TItem> Offset<TItem>(
        IReadOnlyList<TItem> items,
        int page,
        int pageSize,
        int totalCount) =>
        new(items, pageSize, page, totalCount, null);

    public static PagedResult<TItem> Keyset<TItem>(IReadOnlyList<TItem> items, int pageSize, string? nextCursor) =>
        new(items, pageSize, null, null, nextCursor);
}

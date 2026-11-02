using AppTemplate.Application.Core.Common.Results;

namespace AppTemplate.Api.Common.Contracts;

/// <summary>One page of results, and what is needed to ask for the next.</summary>
public sealed record PagedResponse<TItem>(
    IReadOnlyList<TItem> Items,
    int PageSize,
    int? Page,
    int? TotalCount,
    int? TotalPages,
    bool HasNextPage,
    string? NextCursor);

public static class PagedResponse
{
    public static PagedResponse<TItem> From<TSource, TItem>(PagedResult<TSource> page, Func<TSource, TItem> item)
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(item);

        return new PagedResponse<TItem>(
            [.. page.Items.Select(item)],
            page.PageSize,
            page.Page,
            page.TotalCount,
            page.TotalPages,
            page.HasNextPage,
            page.NextCursor);
    }
}

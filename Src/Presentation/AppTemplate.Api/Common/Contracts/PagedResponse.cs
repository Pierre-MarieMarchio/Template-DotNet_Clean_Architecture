using AppTemplate.Application.Common.Results;

namespace AppTemplate.Api.Common.Contracts;

/// <summary>
/// The wire shape of one page, mirroring <see cref="PagedResult{TItem}"/> so that no application
/// type is serialised straight onto the wire.
/// </summary>
/// <remarks>
/// <paramref name="TotalPages"/> and <paramref name="HasNextPage"/> are plain members here, where on
/// the application record they are derived. A client cannot tell the difference, and a contract that
/// re-derived them would be a second copy of a rule that must have exactly one — so the values are
/// carried across rather than recomputed.
/// <para>
/// See <c>docs/adr/0016-pagination-metadata-in-the-body.md</c> for why the metadata travels in the
/// body rather than in <c>Link</c> headers.
/// </para>
/// </remarks>
public sealed record PagedResponse<TItem>(
    IReadOnlyList<TItem> Items,
    int PageSize,
    int? Page,
    int? TotalCount,
    int? TotalPages,
    bool HasNextPage,
    string? NextCursor);

/// <summary>
/// The type-inferring entry point for <see cref="PagedResponse{TItem}"/>, so that a feature's mapper
/// names only the projection of one item and never restates the seven metadata members.
/// </summary>
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

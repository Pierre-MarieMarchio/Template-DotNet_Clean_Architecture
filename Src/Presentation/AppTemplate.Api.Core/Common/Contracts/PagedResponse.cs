using AppTemplate.Application.Core.Common.Results;

namespace AppTemplate.Api.Core.Common.Contracts;

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
/// The metadata travels in the
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
    /// <summary>
    /// Projects a page of domain objects into a page of response items, carrying the paging
    /// metadata across unchanged.
    /// </summary>
    /// <typeparam name="TSource">What the application layer returned.</typeparam>
    /// <typeparam name="TItem">What this API answers with.</typeparam>
    /// <param name="page">The page to project.</param>
    /// <param name="item">How one element becomes one response item.</param>
    /// <returns>The same page, with each element mapped.</returns>
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

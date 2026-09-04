namespace AppTemplate.Application.Features.TodoLists.UseCases.Queries.GetTodoLists;

/// <param name="Paging">"offset" (the default) or "cursor". Blank means offset.</param>
/// <param name="Page">1-based page number. Offset mode only.</param>
/// <param name="PageSize">
/// Defaults to <see cref="Policies.TodoListCollectionPolicy.Instance"/>'s own default page size; must not
/// exceed its ceiling.
/// </param>
/// <param name="Cursor">Opaque, minted by a previous page's <c>nextCursor</c>. Cursor mode only.</param>
/// <param name="Sort">
/// Comma-separated sort terms, e.g. <c>name:asc,createdAt:desc</c>. Cursor mode allows at most one.
/// </param>
/// <param name="Search">Matches the list name, case-insensitively, as a contains.</param>
/// <param name="CreatedAfter">ISO 8601. Inclusive lower bound on <c>createdAt</c>.</param>
/// <param name="CreatedBefore">ISO 8601. Inclusive upper bound on <c>createdAt</c>.</param>
public sealed record GetTodoListsQuery(
    string? Paging,
    int? Page,
    int? PageSize,
    string? Cursor,
    string? Sort,
    string? Search,
    string? CreatedAfter,
    string? CreatedBefore)
{
    /// <summary>The common case: offset paging, nothing sorted, filtered or resumed.</summary>
    public static GetTodoListsQuery Offset(int? page, int? pageSize) =>
        new(null, page, pageSize, null, null, null, null, null);
}

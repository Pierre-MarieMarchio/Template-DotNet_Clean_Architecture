using AppTemplate.Application.Common;
using AppTemplate.Application.Common.Abstractions;
using AppTemplate.Application.Common.Collections;
using AppTemplate.Application.Features.TodoLists.Collections;
using AppTemplate.Application.Features.TodoLists.Dtos;
using AppTemplate.Application.Features.TodoLists.Ports;

namespace AppTemplate.Application.Features.TodoLists.UseCases.Queries;

/// <param name="Paging">"offset" (the default) or "cursor". Blank means offset.</param>
/// <param name="Page">1-based page number. Offset mode only.</param>
/// <param name="PageSize">
/// Defaults to <see cref="TodoListCollectionPolicy.Instance"/>'s own default page size; must not
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

/// <summary>
/// The owner filter comes from <see cref="ICurrentUser"/> and is deliberately not part of the
/// query, so no caller can widen it.
/// </summary>
public interface IGetTodoListsUseCase : IUseCase<GetTodoListsQuery, Result<PagedResult<TodoListSummaryDto>>>;

public sealed class GetTodoListsUseCase(ITodoListQueries queries, ICurrentUser currentUser) : IGetTodoListsUseCase
{
    public async Task<Result<PagedResult<TodoListSummaryDto>>> ExecuteAsync(
        GetTodoListsQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var userId = currentUser.RequireUserId();

        if (userId.IsFailure)
        {
            return userId.To<PagedResult<TodoListSummaryDto>>();
        }

        var bound = TodoListRequestBinder.Bind(query);

        if (bound.IsFailure)
        {
            return bound.To<PagedResult<TodoListSummaryDto>>();
        }

        return await queries.GetForOwnerAsync(userId.Value, bound.Value, cancellationToken);
    }
}

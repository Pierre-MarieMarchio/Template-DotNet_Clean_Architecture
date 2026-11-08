using AppTemplate.Application.Core.Common.Collections;
using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Application.Features.TodoLists.Model.Dtos;
using AppTemplate.Application.Features.TodoLists.Ports.TodoListQueries;
using Mediator;

namespace AppTemplate.Application.Features.TodoLists.UseCases.Queries.GetTodoLists;

public sealed record GetTodoListsQuery(
    string? Paging,
    int? Page,
    int? PageSize,
    string? Cursor,
    string? Sort,
    string? Search,
    string? CreatedAfter,
    string? CreatedBefore) : ICollectionQuery, IQuery<Result<PagedResult<TodoListSummaryDto>>>
{
    public static GetTodoListsQuery Offset(int? page, int? pageSize) =>
        new(null, page, pageSize, null, null, null, null, null);
}

public sealed class GetTodoListsQueryHandler(ITodoListQueries queries, ICurrentUser currentUser) : IQueryHandler<GetTodoListsQuery, Result<PagedResult<TodoListSummaryDto>>>
{
    public async ValueTask<Result<PagedResult<TodoListSummaryDto>>> Handle(
        GetTodoListsQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var userId = currentUser.RequireUserId();

        if (userId.IsFailure)
        {
            return userId.To<PagedResult<TodoListSummaryDto>>();
        }

        var bound = GetTodoListsRequestBinder.Bind(query);

        if (bound.IsFailure)
        {
            return bound.To<PagedResult<TodoListSummaryDto>>();
        }

        return await queries.GetForOwnerAsync(userId.Value, bound.Value, cancellationToken);
    }
}

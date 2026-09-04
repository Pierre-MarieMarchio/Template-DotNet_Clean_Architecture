using AppTemplate.Application.Common.Abstractions;
using AppTemplate.Application.Common.Results;
using AppTemplate.Application.Features.TodoLists.Dtos;
using AppTemplate.Application.Features.TodoLists.Ports.TodoListQueries;

namespace AppTemplate.Application.Features.TodoLists.UseCases.Queries.GetTodoLists;

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

        var bound = GetTodoListsRequestBinder.Bind(query);

        if (bound.IsFailure)
        {
            return bound.To<PagedResult<TodoListSummaryDto>>();
        }

        return await queries.GetForOwnerAsync(userId.Value, bound.Value, cancellationToken);
    }
}

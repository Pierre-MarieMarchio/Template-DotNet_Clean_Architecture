using AppTemplate.Application.Core.Common.Concurrency;
using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Application.Features.TodoLists.Model.Dtos;
using AppTemplate.Application.Features.TodoLists.Model.Errors;
using AppTemplate.Application.Features.TodoLists.Ports.TodoListQueries;
using Mediator;

namespace AppTemplate.Application.Features.TodoLists.UseCases.Queries.GetTodoItems;

public sealed record GetTodoItemsQuery(Guid TodoListId) : IQuery<Result<Versioned<IReadOnlyList<TodoItemDto>>>>;

public sealed class GetTodoItemsQueryHandler(
    ITodoListQueries queries,
    ICurrentUser currentUser) : IQueryHandler<GetTodoItemsQuery, Result<Versioned<IReadOnlyList<TodoItemDto>>>>
{
    public async ValueTask<Result<Versioned<IReadOnlyList<TodoItemDto>>>> Handle(
        GetTodoItemsQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var userId = currentUser.RequireUserId();

        if (userId.IsFailure)
        {
            return userId.To<Versioned<IReadOnlyList<TodoItemDto>>>();
        }

        var detail = await queries.GetDetailAsync(query.TodoListId, userId.Value, cancellationToken);

        if (detail is null)
        {
            return Result.Failure<Versioned<IReadOnlyList<TodoItemDto>>>(
                TodoListErrors.ListNotFound(query.TodoListId));
        }

        return new Versioned<IReadOnlyList<TodoItemDto>>(detail.Value.Items, detail.Version);
    }
}

using AppTemplate.Application.Core.Common.Concurrency;
using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Application.Features.TodoLists.Model.Dtos;
using AppTemplate.Application.Features.TodoLists.Model.Errors;
using AppTemplate.Application.Features.TodoLists.Ports.TodoListQueries;
using Mediator;

namespace AppTemplate.Application.Features.TodoLists.UseCases.Queries.GetTodoItem;

public sealed record GetTodoItemQuery(Guid TodoListId, Guid TodoItemId) : IQuery<Result<Versioned<TodoItemDto>>>;

public sealed class GetTodoItemQueryHandler(
    ITodoListQueries queries,
    ICurrentUser currentUser) : IQueryHandler<GetTodoItemQuery, Result<Versioned<TodoItemDto>>>
{
    public async ValueTask<Result<Versioned<TodoItemDto>>> Handle(
        GetTodoItemQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var userId = currentUser.RequireUserId();

        if (userId.IsFailure)
        {
            return userId.To<Versioned<TodoItemDto>>();
        }

        var detail = await queries.GetDetailAsync(query.TodoListId, userId.Value, cancellationToken);

        if (detail is null)
        {
            return Result.Failure<Versioned<TodoItemDto>>(TodoListErrors.ListNotFound(query.TodoListId));
        }

        var item = detail.Value.Items.FirstOrDefault(candidate => candidate.Id == query.TodoItemId);

        return item is null
            ? Result.Failure<Versioned<TodoItemDto>>(TodoListErrors.ItemNotFound(query.TodoItemId))
            : new Versioned<TodoItemDto>(item, detail.Version);
    }
}

using AppTemplate.Application.Common.Concurrency;
using AppTemplate.Application.Common.Ports;
using AppTemplate.Application.Common.Results;
using AppTemplate.Application.Features.TodoLists.Errors;
using AppTemplate.Domain.Features.TodoLists.Entities;
using AppTemplate.Domain.Features.TodoLists.Repositories;

namespace AppTemplate.Application.Features.TodoLists.Services;

internal sealed class TodoListService(ITodoListRepository repository, ICurrentUser currentUser) : ITodoListService
{
    public async Task<Result<TodoList>> LoadOwnedAsync(
        Guid todoListId,
        VersionPrecondition? precondition,
        CancellationToken cancellationToken = default)
    {
        var userId = currentUser.RequireUserId();

        if (userId.IsFailure)
        {
            return userId.To<TodoList>();
        }

        var ownerId = userId.Value;

        var todoList = await repository.GetAsync(todoListId, cancellationToken);

        if (todoList is null || todoList.OwnerId != ownerId)
        {
            return Result.Failure<TodoList>(TodoListErrors.ListNotFound(todoListId));
        }

        // Compared against the aggregate this call just loaded, so nothing can commit between the
        // comparison and whatever the caller does with the result.
        if (precondition is not null && !precondition.IsSatisfiedBy(todoList.Version))
        {
            return Result.Failure<TodoList>(ConcurrencyErrors.PreconditionFailed);
        }

        return todoList;
    }
}

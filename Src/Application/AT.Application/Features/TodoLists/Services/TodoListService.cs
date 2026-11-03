using AppTemplate.Application.Core.Common.Concurrency;
using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Application.Features.TodoLists.Model.Errors;
using AppTemplate.Domain.Features.TodoLists.Model.Entities;
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

        var aggregate = await repository.GetAsync(userId.Value, todoListId, cancellationToken);

        if (aggregate is null)
        {
            return Result.Failure<TodoList>(TodoListErrors.ListNotFound(todoListId));
        }

        return precondition is not null && !precondition.IsSatisfiedBy(aggregate.Version)
            ? Result.Failure<TodoList>(ConcurrencyErrors.PreconditionFailed)
            : Result.Success(aggregate);
    }
}

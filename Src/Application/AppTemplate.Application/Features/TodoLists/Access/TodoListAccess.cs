using AppTemplate.Application.Common;
using AppTemplate.Application.Common.Abstractions;
using AppTemplate.Application.Common.Concurrency;
using AppTemplate.Application.Features.TodoLists.Errors;
using AppTemplate.Domain.Features.TodoLists.Entities;
using AppTemplate.Domain.Features.TodoLists.Stores;

namespace AppTemplate.Application.Features.TodoLists.Access;

/// <summary>
/// The one gate every to-do list command loads its aggregate through: identity, ownership and
/// the version precondition, in that order, so every use case rejects the same way for the same
/// reasons instead of repeating the three checks slightly differently each time.
/// </summary>
public interface ITodoListAccess
{
    /// <returns>
    /// The aggregate, or a failure — <see cref="TodoListErrors.ListNotFound"/> for an anonymous
    /// caller, an unknown id or somebody else's list, and
    /// <see cref="ConcurrencyErrors.PreconditionFailed"/> once ownership is established but the
    /// caller named a version the aggregate no longer holds.
    /// </returns>
    Task<Result<TodoList>> LoadOwnedAsync(
        Guid todoListId,
        VersionPrecondition? precondition,
        CancellationToken cancellationToken = default);
}

internal sealed class TodoListAccess(ITodoListRepository repository, ICurrentUser currentUser) : ITodoListAccess
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

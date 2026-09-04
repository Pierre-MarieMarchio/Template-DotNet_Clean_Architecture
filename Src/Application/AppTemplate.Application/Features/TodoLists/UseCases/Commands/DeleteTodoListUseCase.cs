using AppTemplate.Application.Common;
using AppTemplate.Application.Common.Abstractions;
using AppTemplate.Application.Features.TodoLists.Concurrency;
using AppTemplate.Application.Features.TodoLists.Errors;
using AppTemplate.Application.Features.TodoLists.Ports;
using AppTemplate.Domain.Features.TodoLists.Stores;

namespace AppTemplate.Application.Features.TodoLists.UseCases.Commands;

/// <summary>The list, and its items and tags with it.</summary>
/// <param name="Precondition">
/// The versions the caller will accept, or <c>null</c> for an unconditional delete.
/// </param>
public sealed record DeleteTodoListCommand(Guid TodoListId, VersionPrecondition? Precondition = null);

public interface IDeleteTodoListUseCase : IUseCase<DeleteTodoListCommand, Result>;

public sealed class DeleteTodoListUseCase(
    ITodoListRepository repository,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser) : IDeleteTodoListUseCase
{
    public async Task<Result> ExecuteAsync(
        DeleteTodoListCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (currentUser.UserId is not { } ownerId)
        {
            return Result.Failure(TodoListErrors.NotAuthenticated);
        }

        var todoList = await repository.GetAsync(command.TodoListId, cancellationToken);

        if (todoList is null || todoList.OwnerId != ownerId)
        {
            return Result.Failure(TodoListErrors.ListNotFound(command.TodoListId));
        }

        if (command.Precondition is { } precondition && !precondition.IsSatisfiedBy(todoList.Version))
        {
            return Result.Failure(TodoListErrors.PreconditionFailed);
        }

        repository.Remove(todoList);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}

using AppTemplate.Application.Common;
using AppTemplate.Application.Common.Abstractions;
using AppTemplate.Application.Features.TodoLists.Concurrency;
using AppTemplate.Application.Features.TodoLists.Errors;
using AppTemplate.Application.Features.TodoLists.Ports;
using AppTemplate.Domain.Features.TodoLists.Stores;

namespace AppTemplate.Application.Features.TodoLists.UseCases.Commands;

/// <param name="Precondition">
/// The versions the caller will accept, or <c>null</c> for an unconditional removal.
/// </param>
public sealed record RemoveTodoItemCommand(
    Guid TodoListId,
    Guid TodoItemId,
    VersionPrecondition? Precondition = null);

public interface IRemoveTodoItemUseCase : IUseCase<RemoveTodoItemCommand, Result>;

public sealed class RemoveTodoItemUseCase(
    ITodoListRepository repository,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser) : IRemoveTodoItemUseCase
{
    public async Task<Result> ExecuteAsync(
        RemoveTodoItemCommand command,
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

        if (!todoList.Items.Any(item => item.Id == command.TodoItemId))
        {
            return Result.Failure(TodoListErrors.ItemNotFound(command.TodoItemId));
        }

        if (command.Precondition is { } precondition && !precondition.IsSatisfiedBy(todoList.Version))
        {
            return Result.Failure(TodoListErrors.PreconditionFailed);
        }

        // No try/catch: existence is the only thing RemoveItem rejects, and it is checked above.
        todoList.RemoveItem(command.TodoItemId);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}

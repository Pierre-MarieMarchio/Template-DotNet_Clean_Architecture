using AppTemplate.Application.Common;
using AppTemplate.Application.Common.Abstractions;
using AppTemplate.Application.Features.TodoLists.Concurrency;
using AppTemplate.Application.Features.TodoLists.Errors;
using AppTemplate.Application.Features.TodoLists.Ports;
using AppTemplate.Domain.Common.Exceptions;
using AppTemplate.Domain.Features.TodoLists.Stores;

namespace AppTemplate.Application.Features.TodoLists.UseCases.Commands;

/// <param name="Precondition">
/// The versions the caller will accept, or <c>null</c> for an unconditional completion.
/// </param>
public sealed record CompleteTodoItemCommand(
    Guid TodoListId,
    Guid TodoItemId,
    VersionPrecondition? Precondition = null);

public interface ICompleteTodoItemUseCase : IUseCase<CompleteTodoItemCommand, Result>;

public sealed class CompleteTodoItemUseCase(
    ITodoListRepository repository,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    IDateTimeProvider dateTimeProvider) : ICompleteTodoItemUseCase
{
    public async Task<Result> ExecuteAsync(
        CompleteTodoItemCommand command,
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

        // Checked here so an unknown id answers 404 rather than the aggregate's 409 throw.
        if (!todoList.Items.Any(item => item.Id == command.TodoItemId))
        {
            return Result.Failure(TodoListErrors.ItemNotFound(command.TodoItemId));
        }

        if (command.Precondition is { } precondition && !precondition.IsSatisfiedBy(todoList.Version))
        {
            return Result.Failure(TodoListErrors.PreconditionFailed);
        }

        try
        {
            todoList.CompleteItem(command.TodoItemId, dateTimeProvider.UtcNow);
        }
        catch (DomainException exception)
        {
            return Result.Failure(TodoListErrors.InvariantViolated(exception.Message));
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}

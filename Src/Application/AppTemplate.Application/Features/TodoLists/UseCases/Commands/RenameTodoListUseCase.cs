using AppTemplate.Application.Common;
using AppTemplate.Application.Common.Abstractions;
using AppTemplate.Application.Features.TodoLists.Concurrency;
using AppTemplate.Application.Features.TodoLists.Errors;
using AppTemplate.Application.Features.TodoLists.Ports;
using AppTemplate.Domain.Features.TodoLists.Stores;
using FluentValidation;

namespace AppTemplate.Application.Features.TodoLists.UseCases.Commands;

/// <param name="Precondition">
/// The versions the caller will accept, or <c>null</c> for an unconditional rename.
/// </param>
public sealed record RenameTodoListCommand(
    Guid TodoListId,
    string Name,
    VersionPrecondition? Precondition = null);

public interface IRenameTodoListUseCase : IUseCase<RenameTodoListCommand, Result>;

public sealed class RenameTodoListUseCase(
    ITodoListRepository repository,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    IValidator<RenameTodoListCommand> validator) : IRenameTodoListUseCase
{
    public async Task<Result> ExecuteAsync(
        RenameTodoListCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (currentUser.UserId is not { } ownerId)
        {
            return Result.Failure(TodoListErrors.NotAuthenticated);
        }

        var validation = await validator.ValidateAsync(command, cancellationToken);

        if (!validation.IsValid)
        {
            return Result.Failure(TodoListErrors.Invalid(validation));
        }

        var todoList = await repository.GetAsync(command.TodoListId, cancellationToken);

        if (todoList is null || todoList.OwnerId != ownerId)
        {
            return Result.Failure(TodoListErrors.ListNotFound(command.TodoListId));
        }

        // Compared against the aggregate this scope just loaded, so nothing can commit between the
        // comparison and the write below.
        if (command.Precondition is { } precondition && !precondition.IsSatisfiedBy(todoList.Version))
        {
            return Result.Failure(TodoListErrors.PreconditionFailed);
        }

        // No try/catch: Rename only rejects names the validator above already rejected, so a
        // DomainException here means the two disagree — a bug to surface, not a user failure.
        todoList.Rename(command.Name);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}

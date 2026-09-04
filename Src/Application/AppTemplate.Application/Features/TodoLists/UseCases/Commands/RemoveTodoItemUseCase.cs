using AppTemplate.Application.Common;
using AppTemplate.Application.Common.Abstractions;
using AppTemplate.Application.Common.Concurrency;
using AppTemplate.Application.Common.Validation;
using AppTemplate.Application.Features.TodoLists.Access;
using AppTemplate.Application.Features.TodoLists.Dtos;
using FluentValidation;

namespace AppTemplate.Application.Features.TodoLists.UseCases.Commands;

/// <param name="Precondition">
/// The versions the caller will accept, or <c>null</c> for an unconditional removal.
/// </param>
public sealed record RemoveTodoItemCommand(
    Guid TodoListId,
    Guid TodoItemId,
    VersionPrecondition? Precondition = null);

public interface IRemoveTodoItemUseCase : IUseCase<RemoveTodoItemCommand, Result<Versioned<TodoListDetailDto>>>;

public sealed class RemoveTodoItemUseCase(
    ITodoListAccess lists,
    IUnitOfWork unitOfWork,
    IValidator<RemoveTodoItemCommand> validator) : IRemoveTodoItemUseCase
{
    public async Task<Result<Versioned<TodoListDetailDto>>> ExecuteAsync(
        RemoveTodoItemCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var validation = await validator.EnsureValidAsync(command, cancellationToken);

        if (validation.IsFailure)
        {
            return validation.To<Versioned<TodoListDetailDto>>();
        }

        var access = await lists.LoadOwnedAsync(command.TodoListId, command.Precondition, cancellationToken);

        if (access.IsFailure)
        {
            return access.To<Versioned<TodoListDetailDto>>();
        }

        var todoList = access.Value;

        var found = todoList.RequireItem(command.TodoItemId);

        if (found.IsFailure)
        {
            return found.To<Versioned<TodoListDetailDto>>();
        }

        // No try/catch: existence is the only thing RemoveItem rejects, and it is checked above.
        todoList.RemoveItem(command.TodoItemId);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        // The item is gone; the list is what is left to describe, under its new version.
        return TodoListProjection.Detail(todoList);
    }
}

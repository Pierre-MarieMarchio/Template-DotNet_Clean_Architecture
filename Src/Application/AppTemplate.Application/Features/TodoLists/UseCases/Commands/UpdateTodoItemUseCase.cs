using AppTemplate.Application.Common;
using AppTemplate.Application.Common.Abstractions;
using AppTemplate.Application.Common.Concurrency;
using AppTemplate.Application.Common.Validation;
using AppTemplate.Application.Features.TodoLists.Access;
using AppTemplate.Application.Features.TodoLists.Dtos;
using FluentValidation;

namespace AppTemplate.Application.Features.TodoLists.UseCases.Commands;

/// <param name="Title">Must stay unique within the list, excluding the item itself.</param>
/// <param name="Precondition">
/// The versions the caller will accept, or <c>null</c> for an unconditional update.
/// </param>
public sealed record UpdateTodoItemCommand(
    Guid TodoListId,
    Guid TodoItemId,
    string Title,
    string? Description,
    VersionPrecondition? Precondition = null);

public interface IUpdateTodoItemUseCase : IUseCase<UpdateTodoItemCommand, Result<Versioned<TodoItemDto>>>;

public sealed class UpdateTodoItemUseCase(
    ITodoListAccess lists,
    IUnitOfWork unitOfWork,
    IValidator<UpdateTodoItemCommand> validator) : IUpdateTodoItemUseCase
{
    public async Task<Result<Versioned<TodoItemDto>>> ExecuteAsync(
        UpdateTodoItemCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var validation = await validator.EnsureValidAsync(command, cancellationToken);

        if (validation.IsFailure)
        {
            return validation.To<Versioned<TodoItemDto>>();
        }

        var access = await lists.LoadOwnedAsync(command.TodoListId, command.Precondition, cancellationToken);

        if (access.IsFailure)
        {
            return access.To<Versioned<TodoItemDto>>();
        }

        var todoList = access.Value;

        var found = todoList.RequireItem(command.TodoItemId);

        if (found.IsFailure)
        {
            return found.To<Versioned<TodoItemDto>>();
        }

        // Caught: a title clash with another item depends on the aggregate's current contents.
        var update = DomainGuard.Try(() =>
            todoList.UpdateItem(command.TodoItemId, command.Title, command.Description));

        if (update.IsFailure)
        {
            return update.To<Versioned<TodoItemDto>>();
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return TodoListProjection.Item(todoList, command.TodoItemId);
    }
}

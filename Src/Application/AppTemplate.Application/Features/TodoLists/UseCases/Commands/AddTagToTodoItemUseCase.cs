using AppTemplate.Application.Common;
using AppTemplate.Application.Common.Abstractions;
using AppTemplate.Application.Common.Concurrency;
using AppTemplate.Application.Common.Validation;
using AppTemplate.Application.Features.TodoLists.Access;
using AppTemplate.Application.Features.TodoLists.Dtos;
using FluentValidation;

namespace AppTemplate.Application.Features.TodoLists.UseCases.Commands;

/// <param name="Precondition">
/// The versions the caller will accept, or <c>null</c> for an unconditional add.
/// </param>
public sealed record AddTagToTodoItemCommand(
    Guid TodoListId,
    Guid TodoItemId,
    string Tag,
    VersionPrecondition? Precondition = null);

public interface IAddTagToTodoItemUseCase : IUseCase<AddTagToTodoItemCommand, Result<Versioned<TodoItemDto>>>;

public sealed class AddTagToTodoItemUseCase(
    ITodoListAccess lists,
    IUnitOfWork unitOfWork,
    IValidator<AddTagToTodoItemCommand> validator) : IAddTagToTodoItemUseCase
{
    public async Task<Result<Versioned<TodoItemDto>>> ExecuteAsync(
        AddTagToTodoItemCommand command,
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

        // Caught: the tag cap depends on how many tags the item already carries.
        var addition = DomainGuard.Try(() => todoList.AddTagToItem(command.TodoItemId, command.Tag));

        if (addition.IsFailure)
        {
            return addition.To<Versioned<TodoItemDto>>();
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return TodoListProjection.Item(todoList, command.TodoItemId);
    }
}

using AppTemplate.Application.Common;
using AppTemplate.Application.Common.Abstractions;
using AppTemplate.Application.Common.Concurrency;
using AppTemplate.Application.Common.Validation;
using AppTemplate.Application.Features.TodoLists.Access;
using AppTemplate.Application.Features.TodoLists.Dtos;
using FluentValidation;

namespace AppTemplate.Application.Features.TodoLists.UseCases.Commands;

/// <param name="Title">Must be unique within the list.</param>
/// <param name="Tags">Normalised and de-duplicated by the domain.</param>
/// <param name="Precondition">
/// The versions the caller will accept, or <c>null</c> for an unconditional add.
/// </param>
public sealed record AddTodoItemCommand(
    Guid TodoListId,
    string Title,
    string? Description,
    IReadOnlyList<string>? Tags,
    VersionPrecondition? Precondition = null);

public interface IAddTodoItemUseCase : IUseCase<AddTodoItemCommand, Result<Versioned<TodoItemDto>>>;

public sealed class AddTodoItemUseCase(
    ITodoListAccess lists,
    IUnitOfWork unitOfWork,
    IValidator<AddTodoItemCommand> validator) : IAddTodoItemUseCase
{
    public async Task<Result<Versioned<TodoItemDto>>> ExecuteAsync(
        AddTodoItemCommand command,
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

        // Caught: a duplicate title or a full list depends on the aggregate's current contents,
        // so the caller could not have avoided it by sending better input. A malformed tag is not
        // caught here any more — the validator above already rejects it as a 400.
        var itemId = DomainGuard.Try(() =>
        {
            var id = todoList.AddItem(command.Title, command.Description);

            foreach (string tag in command.Tags ?? [])
            {
                todoList.AddTagToItem(id, tag);
            }

            return id;
        });

        if (itemId.IsFailure)
        {
            return itemId.To<Versioned<TodoItemDto>>();
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return TodoListProjection.Item(todoList, itemId.Value);
    }
}

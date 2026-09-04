using AppTemplate.Application.Common.Abstractions;
using AppTemplate.Application.Common.Concurrency;
using AppTemplate.Application.Common.Results;
using AppTemplate.Application.Common.Validation;
using AppTemplate.Application.Features.TodoLists.Dtos;
using AppTemplate.Application.Features.TodoLists.Mapping;
using AppTemplate.Application.Features.TodoLists.Services;
using FluentValidation;

namespace AppTemplate.Application.Features.TodoLists.UseCases.Commands.AddTodoItem;

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

        return TodoListDtoMapping.Item(todoList, itemId.Value);
    }
}

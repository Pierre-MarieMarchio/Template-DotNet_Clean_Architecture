using AppTemplate.Application.Core.Common.Concurrency;
using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Application.Core.Common.Validation;
using AppTemplate.Application.Features.TodoLists.Model.Dtos;
using AppTemplate.Application.Features.TodoLists.Model.Mapping;
using AppTemplate.Application.Features.TodoLists.Services;
using Mediator;

namespace AppTemplate.Application.Features.TodoLists.UseCases.Commands.AddTodoItem;

public sealed record AddTodoItemCommand(
    Guid TodoListId,
    string Title,
    string? Description,
    IReadOnlyList<string>? Tags,
    VersionPrecondition? Precondition = null) : ICommand<Result<Versioned<TodoItemDto>>>;

public sealed class AddTodoItemCommandHandler(
    ITodoListService lists,
    IUnitOfWork unitOfWork) : ICommandHandler<AddTodoItemCommand, Result<Versioned<TodoItemDto>>>
{
    public async ValueTask<Result<Versioned<TodoItemDto>>> Handle(
        AddTodoItemCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var access = await lists.LoadOwnedAsync(command.TodoListId, command.Precondition, cancellationToken);

        if (access.IsFailure)
        {
            return access.To<Versioned<TodoItemDto>>();
        }

        var todoList = access.Value;

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

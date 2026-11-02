using AppTemplate.Application.Core.Common.Concurrency;
using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Application.Core.Common.Validation;
using AppTemplate.Application.Features.TodoLists.Helpers;
using AppTemplate.Application.Features.TodoLists.Model.Dtos;
using AppTemplate.Application.Features.TodoLists.Model.Mapping;
using AppTemplate.Application.Features.TodoLists.Services;
using Mediator;

namespace AppTemplate.Application.Features.TodoLists.UseCases.Commands.UpdateTodoItem;

public sealed record UpdateTodoItemCommand(
    Guid TodoListId,
    Guid TodoItemId,
    string Title,
    string? Description,
    VersionPrecondition? Precondition = null) : ICommand<Result<Versioned<TodoItemDto>>>;

public sealed class UpdateTodoItemCommandHandler(
    ITodoListService lists,
    IUnitOfWork unitOfWork) : ICommandHandler<UpdateTodoItemCommand, Result<Versioned<TodoItemDto>>>
{
    public async ValueTask<Result<Versioned<TodoItemDto>>> Handle(
        UpdateTodoItemCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

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

        var update = DomainGuard.Try(() =>
            todoList.UpdateItem(command.TodoItemId, command.Title, command.Description));

        if (update.IsFailure)
        {
            return update.To<Versioned<TodoItemDto>>();
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return TodoListDtoMapping.Item(todoList, command.TodoItemId);
    }
}

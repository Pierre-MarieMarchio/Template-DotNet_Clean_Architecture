using AppTemplate.Application.Core.Common.Concurrency;
using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Application.Features.TodoLists.Helpers;
using AppTemplate.Application.Features.TodoLists.Model.Dtos;
using AppTemplate.Application.Features.TodoLists.Model.Mapping;
using AppTemplate.Application.Features.TodoLists.Services;
using Mediator;

namespace AppTemplate.Application.Features.TodoLists.UseCases.Commands.RemoveTodoItem;

public sealed record RemoveTodoItemCommand(
    Guid TodoListId,
    Guid TodoItemId,
    VersionPrecondition? Precondition = null) : ICommand<Result<Versioned<TodoListDetailDto>>>;

public sealed class RemoveTodoItemCommandHandler(
    ITodoListService lists,
    IUnitOfWork unitOfWork) : ICommandHandler<RemoveTodoItemCommand, Result<Versioned<TodoListDetailDto>>>
{
    public async ValueTask<Result<Versioned<TodoListDetailDto>>> Handle(
        RemoveTodoItemCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

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

        todoList.RemoveItem(command.TodoItemId);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return TodoListDtoMapping.Detail(todoList);
    }
}

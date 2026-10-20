using AppTemplate.Application.Core.Common.Concurrency;
using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Application.Core.Common.Validation;
using AppTemplate.Application.Features.TodoLists.Helpers;
using AppTemplate.Application.Features.TodoLists.Model.Dtos;
using AppTemplate.Application.Features.TodoLists.Model.Mapping;
using AppTemplate.Application.Features.TodoLists.Services;
using FluentValidation;

namespace AppTemplate.Application.Features.TodoLists.UseCases.Commands.RemoveTodoItem;

public sealed class RemoveTodoItemUseCase(
    ITodoListService lists,
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

        todoList.RemoveItem(command.TodoItemId);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return TodoListDtoMapping.Detail(todoList);
    }
}

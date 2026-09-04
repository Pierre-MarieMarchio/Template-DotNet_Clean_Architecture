using AppTemplate.Application.Common;
using AppTemplate.Application.Common.Abstractions;
using AppTemplate.Application.Common.Validation;
using AppTemplate.Application.Features.TodoLists.Dtos;
using AppTemplate.Application.Features.TodoLists.Extensions;
using AppTemplate.Application.Features.TodoLists.Mapping;
using AppTemplate.Application.Features.TodoLists.Services;
using FluentValidation;

namespace AppTemplate.Application.Features.TodoLists.UseCases.Commands.ReopenTodoItem;

public sealed class ReopenTodoItemUseCase(
    ITodoListAccess lists,
    IUnitOfWork unitOfWork,
    IDateTimeProvider dateTimeProvider,
    IValidator<ReopenTodoItemCommand> validator) : IReopenTodoItemUseCase
{
    public async Task<Result<Versioned<TodoItemDto>>> ExecuteAsync(
        ReopenTodoItemCommand command,
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

        // No try/catch: reopening is idempotent, so ReopenItem never rejects.
        todoList.ReopenItem(command.TodoItemId, dateTimeProvider.UtcNow);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return TodoListProjection.Item(todoList, command.TodoItemId);
    }
}

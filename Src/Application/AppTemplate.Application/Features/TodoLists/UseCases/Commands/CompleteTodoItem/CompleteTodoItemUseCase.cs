using AppTemplate.Application.Common.Abstractions;
using AppTemplate.Application.Common.Concurrency;
using AppTemplate.Application.Common.Results;
using AppTemplate.Application.Common.Validation;
using AppTemplate.Application.Features.TodoLists.Dtos;
using AppTemplate.Application.Features.TodoLists.Extensions;
using AppTemplate.Application.Features.TodoLists.Mapping;
using AppTemplate.Application.Features.TodoLists.Services;
using FluentValidation;

namespace AppTemplate.Application.Features.TodoLists.UseCases.Commands.CompleteTodoItem;

public sealed class CompleteTodoItemUseCase(
    ITodoListAccess lists,
    IUnitOfWork unitOfWork,
    IDateTimeProvider dateTimeProvider,
    IValidator<CompleteTodoItemCommand> validator) : ICompleteTodoItemUseCase
{
    public async Task<Result<Versioned<TodoItemDto>>> ExecuteAsync(
        CompleteTodoItemCommand command,
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

        // Checked here so an unknown id answers 404 rather than the aggregate's own throw. This runs
        // after the precondition check, so a stale caller naming an unknown item sees 412 before
        // 404 — correct under RFC 9110, since the precondition is about the list the request
        // identifies, and it also avoids confirming or denying an item's existence to a caller
        // working from an outdated copy of the list.
        var found = todoList.RequireItem(command.TodoItemId);

        if (found.IsFailure)
        {
            return found.To<Versioned<TodoItemDto>>();
        }

        var completion = DomainGuard.Try(() => todoList.CompleteItem(command.TodoItemId, dateTimeProvider.UtcNow));

        if (completion.IsFailure)
        {
            return completion.To<Versioned<TodoItemDto>>();
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return TodoListDtoMapping.Item(todoList, command.TodoItemId);
    }
}

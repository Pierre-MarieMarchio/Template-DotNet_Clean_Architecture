using AppTemplate.Application.Common.Abstractions;
using AppTemplate.Application.Common.Concurrency;
using AppTemplate.Application.Common.Results;
using AppTemplate.Application.Common.Validation;
using AppTemplate.Application.Features.TodoLists.Dtos;
using AppTemplate.Application.Features.TodoLists.Extensions;
using AppTemplate.Application.Features.TodoLists.Mapping;
using AppTemplate.Application.Features.TodoLists.Services;
using FluentValidation;

namespace AppTemplate.Application.Features.TodoLists.UseCases.Commands.ReplaceTodoItemTags;

public sealed class ReplaceTodoItemTagsUseCase(
    ITodoListAccess lists,
    IUnitOfWork unitOfWork,
    IValidator<ReplaceTodoItemTagsCommand> validator) : IReplaceTodoItemTagsUseCase
{
    public async Task<Result<Versioned<TodoItemDto>>> ExecuteAsync(
        ReplaceTodoItemTagsCommand command,
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

        // Caught: adding a tag not already present can still hit the per-item cap.
        var replacement = DomainGuard.Try(() => todoList.SetItemTags(command.TodoItemId, command.Tags));

        if (replacement.IsFailure)
        {
            return replacement.To<Versioned<TodoItemDto>>();
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return TodoListDtoMapping.Item(todoList, command.TodoItemId);
    }
}

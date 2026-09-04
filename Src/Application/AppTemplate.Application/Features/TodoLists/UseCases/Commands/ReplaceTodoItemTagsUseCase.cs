using AppTemplate.Application.Common;
using AppTemplate.Application.Common.Abstractions;
using AppTemplate.Application.Common.Concurrency;
using AppTemplate.Application.Common.Validation;
using AppTemplate.Application.Features.TodoLists.Access;
using AppTemplate.Application.Features.TodoLists.Dtos;
using FluentValidation;

namespace AppTemplate.Application.Features.TodoLists.UseCases.Commands;

/// <param name="Tags">The complete set the item should end up with; anything not in it is removed.</param>
/// <param name="Precondition">
/// The versions the caller will accept, or <c>null</c> for an unconditional replacement.
/// </param>
public sealed record ReplaceTodoItemTagsCommand(
    Guid TodoListId,
    Guid TodoItemId,
    IReadOnlyList<string> Tags,
    VersionPrecondition? Precondition = null);

public interface IReplaceTodoItemTagsUseCase
    : IUseCase<ReplaceTodoItemTagsCommand, Result<Versioned<TodoItemDto>>>;

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

        return TodoListProjection.Item(todoList, command.TodoItemId);
    }
}

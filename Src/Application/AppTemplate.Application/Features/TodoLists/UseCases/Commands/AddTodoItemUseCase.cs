using AppTemplate.Application.Common;
using AppTemplate.Application.Common.Abstractions;
using AppTemplate.Application.Features.TodoLists.Concurrency;
using AppTemplate.Application.Features.TodoLists.Errors;
using AppTemplate.Application.Features.TodoLists.Ports;
using AppTemplate.Domain.Common.Exceptions;
using AppTemplate.Domain.Features.TodoLists.Stores;
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

public interface IAddTodoItemUseCase : IUseCase<AddTodoItemCommand, Result<Guid>>;

public sealed class AddTodoItemUseCase(
    ITodoListRepository repository,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    IValidator<AddTodoItemCommand> validator) : IAddTodoItemUseCase
{
    /// <returns>The id of the new item.</returns>
    public async Task<Result<Guid>> ExecuteAsync(
        AddTodoItemCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (currentUser.UserId is not { } ownerId)
        {
            return Result.Failure<Guid>(TodoListErrors.NotAuthenticated);
        }

        var validation = await validator.ValidateAsync(command, cancellationToken);

        if (!validation.IsValid)
        {
            return Result.Failure<Guid>(TodoListErrors.Invalid(validation));
        }

        var todoList = await repository.GetAsync(command.TodoListId, cancellationToken);

        if (todoList is null || todoList.OwnerId != ownerId)
        {
            return Result.Failure<Guid>(TodoListErrors.ListNotFound(command.TodoListId));
        }

        if (command.Precondition is { } precondition && !precondition.IsSatisfiedBy(todoList.Version))
        {
            return Result.Failure<Guid>(TodoListErrors.PreconditionFailed);
        }

        Guid itemId;

        try
        {
            // Caught: a duplicate title or a full list depends on the aggregate's current
            // contents, so the caller could not have avoided it by sending better input.
            itemId = todoList.AddItem(command.Title, command.Description);

            foreach (string tag in command.Tags ?? [])
            {
                todoList.AddTagToItem(itemId, tag);
            }
        }
        catch (DomainException exception)
        {
            return Result.Failure<Guid>(TodoListErrors.InvariantViolated(exception.Message));
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return itemId;
    }
}

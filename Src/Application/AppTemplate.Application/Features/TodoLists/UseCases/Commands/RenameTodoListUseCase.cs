using AppTemplate.Application.Common;
using AppTemplate.Application.Common.Abstractions;
using AppTemplate.Application.Common.Concurrency;
using AppTemplate.Application.Common.Validation;
using AppTemplate.Application.Features.TodoLists.Access;
using AppTemplate.Application.Features.TodoLists.Dtos;
using FluentValidation;

namespace AppTemplate.Application.Features.TodoLists.UseCases.Commands;

/// <param name="Precondition">
/// The versions the caller will accept, or <c>null</c> for an unconditional rename.
/// </param>
public sealed record RenameTodoListCommand(
    Guid TodoListId,
    string Name,
    VersionPrecondition? Precondition = null);

public interface IRenameTodoListUseCase : IUseCase<RenameTodoListCommand, Result<Versioned<TodoListDetailDto>>>;

public sealed class RenameTodoListUseCase(
    ITodoListAccess lists,
    IUnitOfWork unitOfWork,
    IValidator<RenameTodoListCommand> validator) : IRenameTodoListUseCase
{
    public async Task<Result<Versioned<TodoListDetailDto>>> ExecuteAsync(
        RenameTodoListCommand command,
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

        // No try/catch: Rename only rejects names the validator above already rejected, so a
        // DomainException here means the two disagree — a bug to surface, not a user failure.
        todoList.Rename(command.Name);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return TodoListProjection.Detail(todoList);
    }
}

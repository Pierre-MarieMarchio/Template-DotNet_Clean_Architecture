using AppTemplate.Application.Common;
using AppTemplate.Application.Common.Abstractions;
using AppTemplate.Application.Common.Validation;
using AppTemplate.Application.Features.TodoLists.Services;
using AppTemplate.Domain.Features.TodoLists.Repositories;
using FluentValidation;

namespace AppTemplate.Application.Features.TodoLists.UseCases.Commands.DeleteTodoList;

public sealed class DeleteTodoListUseCase(
    ITodoListAccess lists,
    ITodoListRepository repository,
    IUnitOfWork unitOfWork,
    IValidator<DeleteTodoListCommand> validator) : IDeleteTodoListUseCase
{
    public async Task<Result> ExecuteAsync(DeleteTodoListCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var validation = await validator.EnsureValidAsync(command, cancellationToken);

        if (validation.IsFailure)
        {
            return validation;
        }

        var access = await lists.LoadOwnedAsync(command.TodoListId, command.Precondition, cancellationToken);

        if (access.IsFailure)
        {
            return access;
        }

        // No projection to return: the resource is gone, so there is nothing left to describe.
        repository.Remove(access.Value);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}

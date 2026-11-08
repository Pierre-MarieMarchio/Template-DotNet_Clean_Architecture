using AppTemplate.Application.Core.Common.Concurrency;
using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Application.Features.TodoLists.Services;
using AppTemplate.Domain.Features.TodoLists.Repositories;
using Mediator;

namespace AppTemplate.Application.Features.TodoLists.UseCases.Commands.DeleteTodoList;

public sealed record DeleteTodoListCommand(Guid TodoListId, VersionPrecondition? Precondition = null) : ICommand<Result>;

public sealed class DeleteTodoListCommandHandler(
    ITodoListService lists,
    ITodoListRepository repository,
    IUnitOfWork unitOfWork) : ICommandHandler<DeleteTodoListCommand, Result>
{
    public async ValueTask<Result> Handle(DeleteTodoListCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var access = await lists.LoadOwnedAsync(command.TodoListId, command.Precondition, cancellationToken);

        if (access.IsFailure)
        {
            return access;
        }

        repository.Remove(access.Value);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}

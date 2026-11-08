using AppTemplate.Application.Core.Common.Concurrency;
using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Application.Features.TodoLists.Model.Dtos;
using AppTemplate.Application.Features.TodoLists.Model.Mapping;
using AppTemplate.Application.Features.TodoLists.Services;
using Mediator;

namespace AppTemplate.Application.Features.TodoLists.UseCases.Commands.RenameTodoList;

public sealed record RenameTodoListCommand(
    Guid TodoListId,
    string Name,
    VersionPrecondition? Precondition = null) : ICommand<Result<Versioned<TodoListDetailDto>>>;

public sealed class RenameTodoListCommandHandler(
    ITodoListService lists,
    IUnitOfWork unitOfWork) : ICommandHandler<RenameTodoListCommand, Result<Versioned<TodoListDetailDto>>>
{
    public async ValueTask<Result<Versioned<TodoListDetailDto>>> Handle(
        RenameTodoListCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var access = await lists.LoadOwnedAsync(command.TodoListId, command.Precondition, cancellationToken);

        if (access.IsFailure)
        {
            return access.To<Versioned<TodoListDetailDto>>();
        }

        var todoList = access.Value;

        todoList.Rename(command.Name);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return TodoListDtoMapping.Detail(todoList);
    }
}

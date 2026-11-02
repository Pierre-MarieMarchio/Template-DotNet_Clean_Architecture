using AppTemplate.Application.Core.Common.Concurrency;
using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Application.Features.TodoLists.Model.Dtos;
using AppTemplate.Application.Features.TodoLists.Model.Mapping;
using AppTemplate.Domain.Features.TodoLists.Model.Entities;
using AppTemplate.Domain.Features.TodoLists.Repositories;
using Mediator;

namespace AppTemplate.Application.Features.TodoLists.UseCases.Commands.CreateTodoList;

public sealed record CreateTodoListCommand(string Name) : ICommand<Result<Versioned<TodoListDetailDto>>>;

public sealed class CreateTodoListCommandHandler(
    ITodoListRepository repository,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    IDateTimeProvider dateTimeProvider) : ICommandHandler<CreateTodoListCommand, Result<Versioned<TodoListDetailDto>>>
{
    public async ValueTask<Result<Versioned<TodoListDetailDto>>> Handle(
        CreateTodoListCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var userId = currentUser.RequireUserId();

        if (userId.IsFailure)
        {
            return userId.To<Versioned<TodoListDetailDto>>();
        }

        var todoList = TodoList.Create(userId.Value, command.Name, dateTimeProvider.UtcNow);

        repository.Add(todoList);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return TodoListDtoMapping.Detail(todoList);
    }
}

using AppTemplate.Application.Common;
using AppTemplate.Application.Common.Abstractions;
using AppTemplate.Application.Common.Validation;
using AppTemplate.Application.Features.TodoLists.Dtos;
using AppTemplate.Domain.Features.TodoLists.Entities;
using AppTemplate.Domain.Features.TodoLists.Stores;
using FluentValidation;

namespace AppTemplate.Application.Features.TodoLists.UseCases.Commands;

public sealed record CreateTodoListCommand(string Name);

public interface ICreateTodoListUseCase : IUseCase<CreateTodoListCommand, Result<Versioned<TodoListDetailDto>>>;

public sealed class CreateTodoListUseCase(
    ITodoListRepository repository,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    IDateTimeProvider dateTimeProvider,
    IValidator<CreateTodoListCommand> validator) : ICreateTodoListUseCase
{
    public async Task<Result<Versioned<TodoListDetailDto>>> ExecuteAsync(
        CreateTodoListCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var userId = currentUser.RequireUserId();

        if (userId.IsFailure)
        {
            return userId.To<Versioned<TodoListDetailDto>>();
        }

        var validation = await validator.EnsureValidAsync(command, cancellationToken);

        if (validation.IsFailure)
        {
            return validation.To<Versioned<TodoListDetailDto>>();
        }

        var todoList = TodoList.Create(userId.Value, command.Name, dateTimeProvider.UtcNow);

        repository.Add(todoList);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return TodoListProjection.Detail(todoList);
    }
}

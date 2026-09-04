using AppTemplate.Application.Common;
using AppTemplate.Application.Common.Abstractions;
using AppTemplate.Application.Features.TodoLists.Errors;
using AppTemplate.Application.Features.TodoLists.Ports;
using AppTemplate.Domain.Features.TodoLists.Entities;
using AppTemplate.Domain.Features.TodoLists.Stores;
using FluentValidation;

namespace AppTemplate.Application.Features.TodoLists.UseCases.Commands;

public sealed record CreateTodoListCommand(string Name);

public interface ICreateTodoListUseCase : IUseCase<CreateTodoListCommand, Result<Guid>>;

public sealed class CreateTodoListUseCase(
    ITodoListRepository repository,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    IDateTimeProvider dateTimeProvider,
    IValidator<CreateTodoListCommand> validator) : ICreateTodoListUseCase
{
    public async Task<Result<Guid>> ExecuteAsync(
        CreateTodoListCommand command,
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

        var todoList = TodoList.Create(ownerId, command.Name, dateTimeProvider.UtcNow);

        repository.Add(todoList);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return todoList.Id;
    }
}

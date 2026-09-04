using AppTemplate.Application.Common.Abstractions;
using AppTemplate.Application.Common.Concurrency;
using AppTemplate.Application.Common.Results;
using AppTemplate.Application.Common.Validation;
using AppTemplate.Application.Features.TodoLists.Dtos;
using AppTemplate.Application.Features.TodoLists.Errors;
using AppTemplate.Application.Features.TodoLists.Ports.TodoListQueries;
using FluentValidation;

namespace AppTemplate.Application.Features.TodoLists.UseCases.Queries.GetTodoItems;

public sealed class GetTodoItemsUseCase(
    ITodoListQueries queries,
    ICurrentUser currentUser,
    IValidator<GetTodoItemsQuery> validator) : IGetTodoItemsUseCase
{
    public async Task<Result<Versioned<IReadOnlyList<TodoItemDto>>>> ExecuteAsync(
        GetTodoItemsQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var validation = await validator.EnsureValidAsync(query, cancellationToken);

        if (validation.IsFailure)
        {
            return validation.To<Versioned<IReadOnlyList<TodoItemDto>>>();
        }

        var userId = currentUser.RequireUserId();

        if (userId.IsFailure)
        {
            return userId.To<Versioned<IReadOnlyList<TodoItemDto>>>();
        }

        var detail = await queries.GetDetailAsync(query.TodoListId, userId.Value, cancellationToken);

        if (detail is null)
        {
            return Result.Failure<Versioned<IReadOnlyList<TodoItemDto>>>(
                TodoListErrors.ListNotFound(query.TodoListId));
        }

        return new Versioned<IReadOnlyList<TodoItemDto>>(detail.Value.Items, detail.Version);
    }
}

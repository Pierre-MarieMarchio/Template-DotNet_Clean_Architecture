using AppTemplate.Application.Common;
using AppTemplate.Application.Common.Abstractions;
using AppTemplate.Application.Common.Validation;
using AppTemplate.Application.Features.TodoLists.Dtos;
using AppTemplate.Application.Features.TodoLists.Errors;
using AppTemplate.Application.Features.TodoLists.Ports.TodoListQueries;
using FluentValidation;

namespace AppTemplate.Application.Features.TodoLists.UseCases.Queries.GetTodoItem;

public sealed class GetTodoItemUseCase(
    ITodoListQueries queries,
    ICurrentUser currentUser,
    IValidator<GetTodoItemQuery> validator) : IGetTodoItemUseCase
{
    /// <returns>
    /// The item, carrying the <em>list's</em> version. The aggregate root is the consistency
    /// boundary, so an item has no version of its own to name: adding a sibling changes what a
    /// caller holding this item is allowed to assume.
    /// </returns>
    public async Task<Result<Versioned<TodoItemDto>>> ExecuteAsync(
        GetTodoItemQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var validation = await validator.EnsureValidAsync(query, cancellationToken);

        if (validation.IsFailure)
        {
            return validation.To<Versioned<TodoItemDto>>();
        }

        var userId = currentUser.RequireUserId();

        if (userId.IsFailure)
        {
            return userId.To<Versioned<TodoItemDto>>();
        }

        // Read through the list's projection rather than a port of its own, so the query that
        // finds the item is the same one that enforces ownership.
        var detail = await queries.GetDetailAsync(query.TodoListId, userId.Value, cancellationToken);

        if (detail is null)
        {
            return Result.Failure<Versioned<TodoItemDto>>(TodoListErrors.ListNotFound(query.TodoListId));
        }

        var item = detail.Value.Items.FirstOrDefault(candidate => candidate.Id == query.TodoItemId);

        return item is null
            ? Result.Failure<Versioned<TodoItemDto>>(TodoListErrors.ItemNotFound(query.TodoItemId))
            : new Versioned<TodoItemDto>(item, detail.Version);
    }
}

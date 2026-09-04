using AppTemplate.Application.Common;
using AppTemplate.Application.Common.Abstractions;
using AppTemplate.Application.Features.TodoLists.Dtos;
using AppTemplate.Application.Features.TodoLists.Errors;
using AppTemplate.Application.Features.TodoLists.Ports;

namespace AppTemplate.Application.Features.TodoLists.UseCases.Queries;

public sealed record GetTodoItemQuery(Guid TodoListId, Guid TodoItemId);

public interface IGetTodoItemUseCase : IUseCase<GetTodoItemQuery, Result<Versioned<TodoItemDto>>>;

public sealed class GetTodoItemUseCase(ITodoListQueries queries, ICurrentUser currentUser) : IGetTodoItemUseCase
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

        if (currentUser.UserId is not { } ownerId)
        {
            return Result.Failure<Versioned<TodoItemDto>>(TodoListErrors.NotAuthenticated);
        }

        // Read through the list's projection rather than a port of its own, so the query that
        // finds the item is the same one that enforces ownership.
        var detail = await queries.GetDetailAsync(query.TodoListId, ownerId, cancellationToken);

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

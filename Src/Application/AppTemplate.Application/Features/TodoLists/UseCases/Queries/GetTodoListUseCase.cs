using AppTemplate.Application.Common;
using AppTemplate.Application.Common.Abstractions;
using AppTemplate.Application.Features.TodoLists.Dtos;
using AppTemplate.Application.Features.TodoLists.Errors;
using AppTemplate.Application.Features.TodoLists.Ports;

namespace AppTemplate.Application.Features.TodoLists.UseCases.Queries;

public interface IGetTodoListUseCase : IUseCase<Guid, Result<Versioned<TodoListDetailDto>>>;

public sealed class GetTodoListUseCase(ITodoListQueries queries, ICurrentUser currentUser) : IGetTodoListUseCase
{
    /// <returns>
    /// The list, and the version a caller has to name to change it. Nothing here decides how that
    /// version is published — the transport does, and it is the only layer that should.
    /// </returns>
    public async Task<Result<Versioned<TodoListDetailDto>>> ExecuteAsync(
        Guid todoListId,
        CancellationToken cancellationToken = default)
    {
        if (currentUser.UserId is not { } ownerId)
        {
            return Result.Failure<Versioned<TodoListDetailDto>>(TodoListErrors.NotAuthenticated);
        }

        // Ownership goes into the query, not a check afterwards: fetching first would already have
        // pulled another user's data into memory.
        var detail = await queries.GetDetailAsync(todoListId, ownerId, cancellationToken);

        return detail is null
            ? Result.Failure<Versioned<TodoListDetailDto>>(TodoListErrors.ListNotFound(todoListId))
            : detail;
    }
}

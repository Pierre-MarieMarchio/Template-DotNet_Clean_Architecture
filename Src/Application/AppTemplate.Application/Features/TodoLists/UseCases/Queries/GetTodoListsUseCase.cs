using AppTemplate.Application.Common;
using AppTemplate.Application.Common.Abstractions;
using AppTemplate.Application.Features.TodoLists.Dtos;
using AppTemplate.Application.Features.TodoLists.Errors;
using AppTemplate.Application.Features.TodoLists.Ports;

namespace AppTemplate.Application.Features.TodoLists.UseCases.Queries;

/// <param name="Page">1-based page number.</param>
/// <param name="PageSize">Between 1 and <see cref="GetTodoListsUseCase.MaxPageSize"/>.</param>
public sealed record GetTodoListsQuery(int Page, int PageSize);

/// <summary>
/// The owner filter comes from <see cref="ICurrentUser"/> and is deliberately not part of the
/// query, so no caller can widen it.
/// </summary>
public interface IGetTodoListsUseCase : IUseCase<GetTodoListsQuery, Result<PagedResult<TodoListSummaryDto>>>;

public sealed class GetTodoListsUseCase(ITodoListQueries queries, ICurrentUser currentUser) : IGetTodoListsUseCase
{
    public const int MaxPageSize = 100;

    public async Task<Result<PagedResult<TodoListSummaryDto>>> ExecuteAsync(
        GetTodoListsQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (currentUser.UserId is not { } ownerId)
        {
            return Result.Failure<PagedResult<TodoListSummaryDto>>(TodoListErrors.NotAuthenticated);
        }

        if (query.Page < 1)
        {
            return Result.Failure<PagedResult<TodoListSummaryDto>>(
                TodoListErrors.InvalidPaging("The page number must be 1 or greater."));
        }

        if (query.PageSize is < 1 or > MaxPageSize)
        {
            return Result.Failure<PagedResult<TodoListSummaryDto>>(
                TodoListErrors.InvalidPaging($"The page size must be between 1 and {MaxPageSize}."));
        }

        return await queries.GetForOwnerAsync(ownerId, query.Page, query.PageSize, cancellationToken);
    }
}

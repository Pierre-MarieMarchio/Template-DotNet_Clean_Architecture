using AppTemplate.Application.Core.Common.Collections;
using AppTemplate.Application.Core.Common.Concurrency;
using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Application.Features.TodoLists.Dtos;
using AppTemplate.Application.Features.TodoLists.Policies;
using AppTemplate.Domain.Core.Common.Primitives;
using AppTemplate.Domain.Features.TodoLists.Repositories;

namespace AppTemplate.Application.Features.TodoLists.Ports.TodoListQueries;

/// <summary>
/// Separate from <see cref="ITodoListRepository"/> because reads have the opposite needs to
/// writes: implementations must project straight to DTOs in SQL, with no change tracking.
/// </summary>
public interface ITodoListQueries
{
    /// <summary>
    /// <paramref name="request"/> has already been through <see cref="TodoListCollectionPolicy"/>'s
    /// whitelist, so nothing here re-validates paging, sort or filter — it only translates them.
    /// </summary>
    Task<PagedResult<TodoListSummaryDto>> GetForOwnerAsync(
        UserId ownerId,
        FeaturePageRequest<TodoListFilter> request,
        CancellationToken cancellationToken = default);

    /// <returns>The list and the aggregate's version, or <c>null</c> when it does not exist or is
    /// not owned by <paramref name="ownerId"/> — the two are deliberately indistinguishable, so a
    /// caller cannot use this to probe for other users' list ids.</returns>
    /// <remarks>
    /// The version comes back from the same query as the representation. Reading it separately
    /// would leave a window in which the two disagree, and a validator that does not describe the
    /// body it was sent with is worse than none.
    /// </remarks>
    Task<Versioned<TodoListDetailDto>?> GetDetailAsync(
        Guid id,
        UserId ownerId,
        CancellationToken cancellationToken = default);
}

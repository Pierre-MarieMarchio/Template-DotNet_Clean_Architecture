using AppTemplate.Application.Common;
using AppTemplate.Application.Features.TodoLists.Collections;
using AppTemplate.Application.Features.TodoLists.Dtos;
using AppTemplate.Domain.Features.TodoLists.Stores;

namespace AppTemplate.Application.Features.TodoLists.Ports;

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
        Guid ownerId,
        TodoListPageRequest request,
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
        Guid ownerId,
        CancellationToken cancellationToken = default);
}

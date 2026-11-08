using AppTemplate.Domain.Core.Common.Primitives;

namespace AppTemplate.Application.Features.TodoLists.Ports.TodoItemQueries;

public interface ITodoItemQueries
{
    Task<bool> ExistsForOwnerAsync(
        Guid todoListId,
        Guid todoItemId,
        UserId ownerId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyDictionary<Guid, bool>> GetCompletionStatesAsync(
        IReadOnlyList<Guid> todoItemIds,
        CancellationToken cancellationToken = default);
}

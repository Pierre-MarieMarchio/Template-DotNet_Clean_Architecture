using AppTemplate.Domain.Core.Common.Primitives;

namespace AppTemplate.Application.Features.TodoLists.Ports.TodoItemQueries;

/// <summary>
/// What another feature may ask about a to-do item, and all it may ask. <c>Reminders</c> is the
/// consumer today; it reaches items through these two questions rather than through the list
/// queries or the item rows, so the day it is extracted nothing but this port has to be replaced.
/// <para>
/// Declared here because the answers are <c>TodoLists</c>' to give: which list holds an item, who
/// owns it and whether it is done are facts of this feature's storage that no other feature may
/// read for itself.
/// </para>
/// </summary>
public interface ITodoItemQueries
{
    /// <returns><c>true</c> when the item exists in that list and the list belongs to
    /// <paramref name="ownerId"/>. A missing item, a wrong list and somebody else's item all answer
    /// <c>false</c>, so a caller cannot use this to probe for other users' ids.</returns>
    Task<bool> ExistsForOwnerAsync(
        Guid todoListId,
        Guid todoItemId,
        UserId ownerId,
        CancellationToken cancellationToken = default);

    /// <returns>
    /// Whether each item in <paramref name="todoItemIds"/> is completed. An id absent from the
    /// result means the item no longer exists at all — removed, or its list deleted — which is not
    /// the same fact as completed, and callers are expected to tell the two apart.
    /// </returns>
    Task<IReadOnlyDictionary<Guid, bool>> GetCompletionStatesAsync(
        IReadOnlyList<Guid> todoItemIds,
        CancellationToken cancellationToken = default);
}

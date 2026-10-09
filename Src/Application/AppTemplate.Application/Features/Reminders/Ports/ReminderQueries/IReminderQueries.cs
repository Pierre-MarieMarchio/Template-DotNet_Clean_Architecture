using AppTemplate.Application.Features.Reminders.Dtos;
using AppTemplate.Domain.Core.Common.Primitives;

namespace AppTemplate.Application.Features.Reminders.Ports.ReminderQueries;

/// <summary>
/// The read side of reminders, separate from <c>IReminderRepository</c> for the reason every
/// feature separates them: a read projects straight to DTOs in SQL and filters there, where the
/// repository loads aggregates, tracks them and leaves the filtering to the caller.
/// </summary>
public interface IReminderQueries
{
    /// <returns>
    /// The reminders <paramref name="ownerId"/> has scheduled for one item of one list, whatever
    /// their state, soonest first. Empty — never an error — when the item does not exist or is
    /// somebody else's: a reminder outlives its item, and a stranger sees the same answer whether
    /// the item is theirs, somebody else's or nobody's.
    /// </returns>
    Task<IReadOnlyList<ReminderDto>> GetForTodoItemAsync(
        UserId ownerId,
        Guid todoListId,
        Guid todoItemId,
        CancellationToken cancellationToken = default);
}

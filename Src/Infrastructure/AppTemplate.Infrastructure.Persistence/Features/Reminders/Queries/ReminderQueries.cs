using AppTemplate.Application.Features.Reminders.Dtos;
using AppTemplate.Application.Features.Reminders.Ports.ReminderQueries;
using AppTemplate.Domain.Core.Common.Primitives;
using AppTemplate.Infrastructure.Persistence.Common.Contexts;
using Microsoft.EntityFrameworkCore;

namespace AppTemplate.Infrastructure.Persistence.Features.Reminders.Queries;

/// <summary>
/// The adapter for <see cref="IReminderQueries"/>: the owner, list and item filters and the ordering
/// run in SQL, and the rows are projected straight to DTOs with nothing tracked. Internal and
/// sealed like every query adapter here; callers depend on the port.
/// </summary>
internal sealed class ReminderQueries(AppDbContext context) : IReminderQueries
{
    public async Task<IReadOnlyList<ReminderDto>> GetForTodoItemAsync(
        UserId ownerId,
        Guid todoListId,
        Guid todoItemId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(ownerId);

        return await context.Reminders
            .AsNoTracking()
            .Where(reminder => reminder.OwnerId == ownerId.Value
                && reminder.TodoListId == todoListId
                && reminder.TodoItemId == todoItemId)
            .OrderBy(reminder => reminder.DueAt)
            .Select(reminder => new ReminderDto(
                reminder.Id,
                reminder.TodoListId,
                reminder.TodoItemId,
                reminder.DueAt,
                reminder.State,
                reminder.NotifiedAt))
            .ToListAsync(cancellationToken);
    }
}

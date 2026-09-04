using AppTemplate.Application.Features.Reminders.Ports.ReminderTargets;
using AppTemplate.Infrastructure.Persistence.Common.Contexts;
using Microsoft.EntityFrameworkCore;

namespace AppTemplate.Infrastructure.Persistence.Features.Reminders.Queries;

/// <summary>
/// The adapter for <see cref="IReminderTargets"/>: one query for a whole batch of item ids, read
/// through <c>TodoListRecord.Items</c> — never <c>Set&lt;TodoItemRecord&gt;()</c> directly — the
/// only way in, exactly as <c>TodoItemRecordConfiguration</c> intends by giving that record no
/// <c>DbSet</c> of its own.
/// <para>
/// Internal and sealed, like every other query adapter in this project: nothing outside it names
/// the type, only <see cref="IReminderTargets"/>.
/// </para>
/// </summary>
internal sealed class ReminderTargets(AppDbContext context) : IReminderTargets
{
    public async Task<IReadOnlyDictionary<Guid, bool>> GetCompletionStatesAsync(
        IReadOnlyList<Guid> todoItemIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(todoItemIds);

        if (todoItemIds.Count == 0)
        {
            return new Dictionary<Guid, bool>();
        }

        var rows = await context.TodoLists
            .AsNoTracking()
            .SelectMany(list => list.Items)
            .Where(item => todoItemIds.Contains(item.Id))
            .Select(item => new { item.Id, IsCompleted = item.CompletedAt != null })
            .ToListAsync(cancellationToken);

        return rows.ToDictionary(row => row.Id, row => row.IsCompleted);
    }
}

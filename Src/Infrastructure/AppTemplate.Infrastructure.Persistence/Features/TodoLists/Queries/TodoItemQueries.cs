using AppTemplate.Application.Features.TodoLists.Ports.TodoItemQueries;
using AppTemplate.Domain.Core.Common.Primitives;
using AppTemplate.Infrastructure.Persistence.Common.Contexts;
using Microsoft.EntityFrameworkCore;

namespace AppTemplate.Infrastructure.Persistence.Features.TodoLists.Queries;

/// <summary>
/// The adapter for <see cref="ITodoItemQueries"/>. Items are read through
/// <c>TodoListRecord.Items</c> — never <c>Set&lt;TodoItemRecord&gt;()</c> — the only way in, exactly
/// as <c>TodoItemRecordConfiguration</c> intends by giving that record no <c>DbSet</c> of its own.
/// <para>
/// It lives in this feature's folder because it reads this feature's rows: the feature that asks
/// the questions never touches them.
/// </para>
/// </summary>
internal sealed class TodoItemQueries(AppDbContext context) : ITodoItemQueries
{
    public Task<bool> ExistsForOwnerAsync(
        Guid todoListId,
        Guid todoItemId,
        UserId ownerId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(ownerId);

        // Owner and list in the predicate, not compared afterwards: another user's row is never read.
        return context.TodoLists
            .AsNoTracking()
            .Where(list => list.Id == todoListId && list.OwnerId == ownerId.Value)
            .SelectMany(list => list.Items)
            .AnyAsync(item => item.Id == todoItemId, cancellationToken);
    }

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

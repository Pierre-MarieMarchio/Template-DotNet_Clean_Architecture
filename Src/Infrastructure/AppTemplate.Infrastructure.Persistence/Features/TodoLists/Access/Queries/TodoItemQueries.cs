using AppTemplate.Application.Features.TodoLists.Ports.TodoItemQueries;
using AppTemplate.Domain.Core.Common.Primitives;
using AppTemplate.Infrastructure.Persistence.Common.Contexts;
using Microsoft.EntityFrameworkCore;

namespace AppTemplate.Infrastructure.Persistence.Features.TodoLists.Access.Queries;

internal sealed class TodoItemQueries(AppDbContext context) : ITodoItemQueries
{
    public Task<bool> ExistsForOwnerAsync(
        Guid todoListId,
        Guid todoItemId,
        UserId ownerId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(ownerId);

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

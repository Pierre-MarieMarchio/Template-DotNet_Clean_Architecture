using AppTemplate.Application.Features.TodoLists.Ports.TodoItemTagQueries;
using AppTemplate.Infrastructure.Persistence.Common.Contexts;
using Microsoft.EntityFrameworkCore;

namespace AppTemplate.Infrastructure.Persistence.Features.TodoLists.Queries;

/// <summary>
/// The adapter for <see cref="ITodoItemTagQueries"/>. One column of a join, projected in SQL:
/// nothing is materialised or tracked.
/// </summary>
internal sealed class TodoItemTagQueries(AppDbContext context) : ITodoItemTagQueries
{
    public async Task<IReadOnlyList<string>> GetUsedTagsForOwnerAsync(
        Guid ownerId,
        CancellationToken cancellationToken = default) =>
        await context.TodoLists
            .AsNoTracking()
            .Where(list => list.OwnerId == ownerId)
            .SelectMany(list => list.Items)
            .SelectMany(item => item.Tags)
            .Select(tag => tag.Value)
            .Distinct()
            .OrderBy(value => value)
            .ToListAsync(cancellationToken);
}

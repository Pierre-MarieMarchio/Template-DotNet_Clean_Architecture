using AppTemplate.Application.Common;
using AppTemplate.Application.Features.TodoLists.Dtos;
using AppTemplate.Application.Features.TodoLists.Ports;
using AppTemplate.Infrastructure.Persistence.Common.Contexts;
using Microsoft.EntityFrameworkCore;

namespace AppTemplate.Infrastructure.Persistence.Features.TodoLists.Queries;

/// <summary>
/// The read side. Every method projects straight from the persistence models into a DTO, so the SQL
/// selects only the columns the DTO needs and nothing is ever materialised or tracked — the previous
/// implementation loaded whole tables and mapped them in memory, one object at a time.
/// <para>
/// This is the half of the split that got <em>simpler</em>. When EF mapped the domain types, a read had
/// to project through them: <c>list.Name.Value</c> reached into a complex type, and every projection
/// depended on a value object staying expressible in LINQ. Reading from rows, the projection is
/// ordinary SQL over ordinary columns, and neither the aggregate nor the mapper is involved at all.
/// </para>
/// <para>
/// Internal and sealed, like the repository: it is the adapter for <see cref="ITodoListQueries"/> and
/// nothing outside this assembly names it. It reads through the context and never writes.
/// </para>
/// </summary>
internal sealed class TodoListQueries(AppDbContext context) : ITodoListQueries
{
    public async Task<PagedResult<TodoListSummaryDto>> GetForOwnerAsync(
        Guid ownerId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        // Without these the offset arithmetic throws from inside the query builder, which names neither
        // argument. The ceiling on the page size is policy and belongs to GetTodoListsUseCase, which
        // rejects anything above its MaxPageSize before a query is ever built.
        ArgumentOutOfRangeException.ThrowIfLessThan(page, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(pageSize, 1);

        var owned = context.TodoLists
            .AsNoTracking()
            .Where(list => list.OwnerId == ownerId);

        // Counted server-side. Materialising the page and calling Count() on the result
        // would report the page's size, not the total, which is the classic pagination bug.
        int totalCount = await owned.CountAsync(cancellationToken);

        var items = await owned
            // Ordering by a unique tiebreaker as well as by date: without it, two lists
            // created in the same instant can swap places between pages, so a row is shown
            // twice and another never at all.
            .OrderByDescending(list => list.CreatedAt)
            .ThenBy(list => list.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(list => new TodoListSummaryDto(
                list.Id,
                list.Name,
                list.Items.Count,
                list.Items.Count(item => item.CompletedAt != null),
                list.CreatedAt))
            .ToListAsync(cancellationToken);

        return new PagedResult<TodoListSummaryDto>(items, page, pageSize, totalCount);
    }

    /// <remarks>
    /// <c>xmin</c> is selected by the same statement as the representation, so the version describes
    /// exactly the rows that were read. Fetching it in a second query would leave a window in which
    /// the two disagree.
    /// </remarks>
    public Task<Versioned<TodoListDetailDto>?> GetDetailAsync(
        Guid id,
        Guid ownerId,
        CancellationToken cancellationToken = default) =>
        context.TodoLists
            .AsNoTracking()
            // Ownership is in the WHERE clause. A query that fetched by id and compared the
            // owner afterwards would have already read another user's row into this process.
            .Where(list => list.Id == id && list.OwnerId == ownerId)
            .Select(list => new Versioned<TodoListDetailDto>(
                new TodoListDetailDto(
                    list.Id,
                    list.Name,
                    list.CreatedAt,
                    list.LastModifiedAt,
                    list.Items
                        .OrderBy(item => item.Title)
                        .Select(item => new TodoItemDto(
                            item.Id,
                            item.Title,
                            item.Description,
                            item.CompletedAt != null,
                            item.CompletedAt,
                            item.Tags.Select(tag => tag.Value).ToList()))
                        .ToList()),
                list.Version))
            .FirstOrDefaultAsync(cancellationToken);
}

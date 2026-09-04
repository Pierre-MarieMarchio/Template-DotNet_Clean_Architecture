using System.Linq.Expressions;
using AppTemplate.Application.Common.Collections;
using AppTemplate.Application.Common.Concurrency;
using AppTemplate.Application.Common.Results;
using AppTemplate.Application.Features.TodoLists.Dtos;
using AppTemplate.Application.Features.TodoLists.Ports.TodoListQueries;
using AppTemplate.Infrastructure.Persistence.Common.Contexts;
using AppTemplate.Infrastructure.Persistence.Features.TodoLists.Models;
using Microsoft.EntityFrameworkCore;

namespace AppTemplate.Infrastructure.Persistence.Features.TodoLists.Queries;

/// <summary>
/// The read side. Every method projects straight from the persistence models into a DTO, so the SQL
/// selects only the columns the DTO needs and nothing is ever materialised or tracked.
/// <para>
/// Internal and sealed, like the repository: it is the adapter for <see cref="ITodoListQueries"/> and
/// nothing outside this assembly names it. It reads through the context and never writes.
/// </para>
/// </summary>
internal sealed class TodoListQueries(AppDbContext context) : ITodoListQueries
{
    public async Task<PagedResult<TodoListSummaryDto>> GetForOwnerAsync(
        Guid ownerId,
        TodoListPageRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Ownership is in the WHERE clause: every read filters by owner, whatever the caller's
        // sort, filter or cursor claims.
        var owned = context.TodoLists
            .AsNoTracking()
            .Where(list => list.OwnerId == ownerId);

        var filtered = ApplyFilter(owned, request.Filter);

        return request.Paging.Mode == PagingMode.Offset
            ? await GetOffsetPageAsync(filtered, request, cancellationToken)
            : await GetKeysetPageAsync(filtered, request, cancellationToken);
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

    private static IQueryable<TodoListRecord> ApplyFilter(IQueryable<TodoListRecord> source, TodoListFilter filter)
    {
        if (filter.Search is { } search)
        {
            string pattern = TodoListLikePattern.Contains(search.Value);
            source = source.Where(list => EF.Functions.ILike(list.Name, pattern, "\\"));
        }

        if (filter.CreatedAfter is { } after)
        {
            source = source.Where(list => list.CreatedAt >= after);
        }

        if (filter.CreatedBefore is { } before)
        {
            source = source.Where(list => list.CreatedAt <= before);
        }

        return source;
    }

    private static async Task<PagedResult<TodoListSummaryDto>> GetOffsetPageAsync(
        IQueryable<TodoListRecord> filtered,
        TodoListPageRequest request,
        CancellationToken cancellationToken)
    {
        // Counted server-side. Materialising the page and calling Count() on the result would
        // report the page's size, not the total, which is the classic pagination bug.
        int totalCount = await filtered.CountAsync(cancellationToken);

        int page = request.Paging.Page!.Value;
        int pageSize = request.Paging.PageSize;

        var items = await TodoListSortMap.ApplyOrder(filtered, request.Sort)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(_toSummary)
            .ToListAsync(cancellationToken);

        return PagedResult.Offset(items, page, pageSize, totalCount);
    }

    private static async Task<PagedResult<TodoListSummaryDto>> GetKeysetPageAsync(
        IQueryable<TodoListRecord> filtered,
        TodoListPageRequest request,
        CancellationToken cancellationToken)
    {
        // Cursor mode never carries more than one sort term — the use case already refuses a
        // multi-term sort under paging=cursor — so this is always the one term to compare against.
        var term = request.Sort.Terms[0];
        int pageSize = request.Paging.PageSize;

        var keysetSource = request.Paging.Cursor is { } cursor
            ? TodoListSortMap.ApplyKeyset(filtered, term, cursor)
            : filtered;

        // One extra row is how "is there a next page" is answered without a second query.
        var items = await TodoListSortMap.ApplyOrder(keysetSource, request.Sort)
            .Take(pageSize + 1)
            .Select(_toSummary)
            .ToListAsync(cancellationToken);

        bool hasNext = items.Count > pageSize;
        var page = hasNext ? items.GetRange(0, pageSize) : items;

        string? nextCursor = null;

        if (hasNext)
        {
            // The cursor names the last row this page actually served, read off the projection —
            // nothing is materialised to produce it.
            var last = page[^1];

            nextCursor = Cursor.After(term, TodoListSortMap.KeyOf(last, term.Field), last.Id).Encode();
        }

        return PagedResult.Keyset(page, pageSize, nextCursor);
    }

    private static readonly Expression<Func<TodoListRecord, TodoListSummaryDto>> _toSummary =
        list => new TodoListSummaryDto(
            list.Id,
            list.Name,
            list.Items.Count,
            list.Items.Count(item => item.CompletedAt != null),
            list.CreatedAt);
}

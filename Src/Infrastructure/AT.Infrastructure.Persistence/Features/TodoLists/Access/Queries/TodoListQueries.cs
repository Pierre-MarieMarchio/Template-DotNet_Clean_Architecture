using System.Linq.Expressions;
using AppTemplate.Application.Core.Common.Collections;
using AppTemplate.Application.Core.Common.Concurrency;
using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Application.Features.TodoLists.Model.Dtos;
using AppTemplate.Application.Features.TodoLists.Ports.TodoListQueries;
using AppTemplate.Domain.Core.Common.Primitives;
using AppTemplate.Infrastructure.Persistence.Common.Contexts;
using AppTemplate.Infrastructure.Persistence.Features.TodoLists.Rows.Models;
using Microsoft.EntityFrameworkCore;

namespace AppTemplate.Infrastructure.Persistence.Features.TodoLists.Access.Queries;

internal sealed class TodoListQueries(AppDbContext context) : ITodoListQueries
{
    public async Task<PagedResult<TodoListSummaryDto>> GetForOwnerAsync(
        UserId ownerId,
        FeaturePageRequest<TodoListFilter> request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var owned = context.TodoLists
            .AsNoTracking()
            .Where(list => list.OwnerId == ownerId.Value);

        var filtered = ApplyFilter(owned, request.Filter);

        return request.Paging.Mode == PagingMode.Offset
            ? await GetOffsetPageAsync(filtered, request, cancellationToken)
            : await GetKeysetPageAsync(filtered, request, cancellationToken);
    }

    public Task<Versioned<TodoListDetailDto>?> GetDetailAsync(
        Guid id,
        UserId ownerId,
        CancellationToken cancellationToken = default) =>
        context.TodoLists
            .AsNoTracking()
            .Where(list => list.Id == id && list.OwnerId == ownerId.Value)
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
            source = source.Where(list => EF.Functions.Like(list.Name.ToLower(), EF.Parameter(pattern).ToLower(), "\\"));
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
        FeaturePageRequest<TodoListFilter> request,
        CancellationToken cancellationToken)
    {
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
        FeaturePageRequest<TodoListFilter> request,
        CancellationToken cancellationToken)
    {
        var term = request.Sort.Terms[0];
        int pageSize = request.Paging.PageSize;

        var keysetSource = request.Paging.Cursor is { } cursor
            ? TodoListSortMap.ApplyKeyset(filtered, term, cursor)
            : filtered;

        var items = await TodoListSortMap.ApplyOrder(keysetSource, request.Sort)
            .Take(pageSize + 1)
            .Select(_toSummary)
            .ToListAsync(cancellationToken);

        bool hasNext = items.Count > pageSize;
        var page = hasNext ? items.GetRange(0, pageSize) : items;

        string? nextCursor = null;

        if (hasNext)
        {
            var last = page[^1];

            nextCursor = Cursor.After(term, TodoListSortMap.KeyOf(last, term.Field), last.Id).Encode();
        }

        return PagedResult.Keyset(page, pageSize, nextCursor);
    }

    public async Task<IReadOnlyList<UserId>> ListDataOwnerIdsAsync(
        CancellationToken cancellationToken = default)
    {
        var ids = await context.TodoLists
            .AsNoTracking()
            .Select(list => list.OwnerId)
            .Distinct()
            .ToListAsync(cancellationToken);

        return [.. ids.Select(UserId.Create)];
    }

    private static readonly Expression<Func<TodoListRecord, TodoListSummaryDto>> _toSummary =
        list => new TodoListSummaryDto(
            list.Id,
            list.Name,
            list.Items.Count,
            list.Items.Count(item => item.CompletedAt != null),
            list.CreatedAt);
}

using System.Linq.Expressions;
using AppTemplate.Application.Core.Common.Collections;
using AppTemplate.Application.Core.Common.Concurrency;
using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Application.Features.Notes.Model.Dtos;
using AppTemplate.Application.Features.Notes.Ports.NoteQueries;
using AppTemplate.Domain.Core.Common.Primitives;
using AppTemplate.Infrastructure.Persistence.Common.Contexts;
using AppTemplate.Infrastructure.Persistence.Features.Notes.Rows.Models;
using Microsoft.EntityFrameworkCore;

namespace AppTemplate.Infrastructure.Persistence.Features.Notes.Access.Queries;

/// <summary>
/// The read side: every method projects straight from <see cref="NoteRecord"/> into a DTO, so the SQL
/// selects only the columns the DTO needs and nothing is tracked. Every query names the owner, so a
/// missing note and somebody else's are the same empty answer and no row of another user is read
/// first and compared afterwards.
/// </summary>
internal sealed class NoteQueries(AppDbContext context) : INoteQueries
{
    public async Task<PagedResult<NoteSummaryDto>> GetForOwnerAsync(
        UserId ownerId,
        FeaturePageRequest<NoteFilter> request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(ownerId);
        ArgumentNullException.ThrowIfNull(request);

        var owned = context.Notes
            .AsNoTracking()
            .Where(note => note.OwnerId == ownerId.Value);

        return request.Paging.Mode == PagingMode.Offset
            ? await GetOffsetPageAsync(owned, request, cancellationToken)
            : await GetKeysetPageAsync(owned, request, cancellationToken);
    }

    /// <remarks>
    /// <c>xmin</c> is selected by the same statement as the representation, so the version describes
    /// exactly the row that was read.
    /// </remarks>
    public Task<Versioned<NoteDto>?> GetAsync(
        UserId ownerId,
        Guid noteId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(ownerId);

        return context.Notes
            .AsNoTracking()
            .Where(note => note.Id == noteId && note.OwnerId == ownerId.Value)
            .Select(note => new Versioned<NoteDto>(
                new NoteDto(note.Id, note.Title, note.Body, note.CreatedAt, note.LastModifiedAt),
                note.Version))
            .FirstOrDefaultAsync(cancellationToken);
    }

    private static async Task<PagedResult<NoteSummaryDto>> GetOffsetPageAsync(
        IQueryable<NoteRecord> owned,
        FeaturePageRequest<NoteFilter> request,
        CancellationToken cancellationToken)
    {
        int totalCount = await owned.CountAsync(cancellationToken);

        int page = request.Paging.Page!.Value;
        int pageSize = request.Paging.PageSize;

        var items = await NoteSortMap.ApplyOrder(owned, request.Sort)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(_toSummary)
            .ToListAsync(cancellationToken);

        return PagedResult.Offset(items, page, pageSize, totalCount);
    }

    private static async Task<PagedResult<NoteSummaryDto>> GetKeysetPageAsync(
        IQueryable<NoteRecord> owned,
        FeaturePageRequest<NoteFilter> request,
        CancellationToken cancellationToken)
    {
        // The service refuses a multi-term sort under paging=cursor, so there is exactly one term.
        var term = request.Sort.Terms[0];
        int pageSize = request.Paging.PageSize;

        var keysetSource = request.Paging.Cursor is { } cursor
            ? NoteSortMap.ApplyKeyset(owned, term, cursor)
            : owned;

        // One row beyond the page answers "is there a next page" without a second query.
        var items = await NoteSortMap.ApplyOrder(keysetSource, request.Sort)
            .Take(pageSize + 1)
            .Select(_toSummary)
            .ToListAsync(cancellationToken);

        bool hasNext = items.Count > pageSize;
        var page = hasNext ? items.GetRange(0, pageSize) : items;

        string? nextCursor = null;

        if (hasNext)
        {
            var last = page[^1];

            nextCursor = Cursor.After(term, NoteSortMap.KeyOf(last, term.Field), last.Id).Encode();
        }

        return PagedResult.Keyset(page, pageSize, nextCursor);
    }

    /// <remarks>
    /// The one query here that crosses owners on purpose: it exists so a housekeeping pass can ask
    /// which ids still hold data, and returns ids only. The primitive is built client-side, because
    /// its factory validates and validation is not SQL.
    /// </remarks>
    public async Task<IReadOnlyList<UserId>> ListDataOwnerIdsAsync(
        CancellationToken cancellationToken = default)
    {
        var ids = await context.Notes
            .AsNoTracking()
            .Select(note => note.OwnerId)
            .Distinct()
            .ToListAsync(cancellationToken);

        return [.. ids.Select(UserId.Create)];
    }

    private static readonly Expression<Func<NoteRecord, NoteSummaryDto>> _toSummary =
        note => new NoteSummaryDto(note.Id, note.Title, note.CreatedAt, note.LastModifiedAt);
}

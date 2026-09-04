using System.Linq.Expressions;
using AppTemplate.Application.Common.Collections;
using AppTemplate.Application.Common.Concurrency;
using AppTemplate.Application.Common.Results;
using AppTemplate.Application.Features.Files.Dtos;
using AppTemplate.Application.Features.Files.Ports.StoredFileQueries;
using AppTemplate.Domain.Features.Files.ValueObjects;
using AppTemplate.Infrastructure.Persistence.Common.Contexts;
using AppTemplate.Infrastructure.Persistence.Features.Files.Models;
using Microsoft.EntityFrameworkCore;

namespace AppTemplate.Infrastructure.Persistence.Features.Files.Queries;

/// <summary>
/// The read side. Every method projects straight from the persistence model into a DTO or a scalar, so
/// the SQL selects only what the answer needs and nothing is ever materialised or tracked.
/// <para>
/// Internal and sealed, like the repository: it is the adapter for <see cref="IStoredFileQueries"/> and
/// nothing outside this assembly names it. It reads through the context and never writes.
/// </para>
/// </summary>
internal sealed class StoredFileQueries(AppDbContext context) : IStoredFileQueries
{
    public async Task<PagedResult<StoredFileDto>> GetForOwnerAsync(
        Guid ownerId,
        StoredFilePageRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Ownership is in the WHERE clause: every read filters by owner, whatever the caller's sort,
        // filter or cursor claims.
        var owned = context.StoredFiles
            .AsNoTracking()
            .Where(file => file.OwnerId == ownerId);

        var filtered = ApplyFilter(owned, request.Filter);

        return request.Paging.Mode == PagingMode.Offset
            ? await GetOffsetPageAsync(filtered, request, cancellationToken)
            : await GetKeysetPageAsync(filtered, request, cancellationToken);
    }

    /// <remarks>
    /// <c>xmin</c> is selected by the same statement as the representation, so the version describes
    /// exactly the row that was read. Fetching it in a second query would leave a window in which the
    /// two disagree.
    /// </remarks>
    public Task<Versioned<StoredFileDto>?> GetDetailAsync(
        Guid id,
        Guid ownerId,
        CancellationToken cancellationToken = default) =>
        context.StoredFiles
            .AsNoTracking()
            // Ownership is in the WHERE clause. A query that fetched by id and compared the owner
            // afterwards would have already read another user's row into this process — and the port
            // promises the two failures are indistinguishable, which only holds if one query answers
            // both.
            .Where(file => file.Id == id && file.OwnerId == ownerId)
            .Select(file => new Versioned<StoredFileDto>(
                new StoredFileDto(
                    file.Id,
                    file.Name,
                    file.DeclaredMediaType,
                    file.SizeInBytes,
                    file.Checksum,
                    file.State,
                    file.RegisteredAt,
                    file.AvailableAt),
                file.Version))
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<OwnerStorageUsage> GetUsageForOwnerAsync(
        Guid ownerId,
        CancellationToken cancellationToken = default)
    {
        // Grouped by state, so one round trip returns at most one row per state however many files
        // the owner has. Counting and summing in the database is the whole point: a quota check that
        // materialised every aggregate to add up four numbers would cost more than the upload it
        // guards.
        var totals = await context.StoredFiles
            .AsNoTracking()
            .Where(file => file.OwnerId == ownerId)
            .GroupBy(file => file.State)
            .Select(group => new
            {
                State = group.Key,
                Count = group.Count(),
                Bytes = group.Sum(file => file.SizeInBytes),
            })
            .ToListAsync(cancellationToken);

        // Every state whose bytes are on the store, which is every state but Pending. Written as
        // "not Pending" rather than as a list of three, so a state added to the enum weighs on the
        // quota on the day it is added instead of on the day somebody remembers this line — the
        // direction StoredFileState's own remarks call refusing by default.
        var stored = totals.Where(total => total.State != StoredFileState.Pending).ToList();
        var pending = totals.FirstOrDefault(total => total.State == StoredFileState.Pending);

        // An owner with no file of one state has no row for it, which is not the same as a zero the
        // database returned — hence the defaults here rather than a query shaped to always produce a
        // row per state.
        return new OwnerStorageUsage(
            stored.Sum(total => total.Count),
            stored.Sum(total => total.Bytes),
            pending?.Count ?? 0,
            pending?.Bytes ?? 0L);
    }

    public async Task<IReadOnlyList<string>> GetLiveObjectKeysAsync(
        IReadOnlyList<string> candidateObjectKeys,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(candidateObjectKeys);

        if (candidateObjectKeys.Count == 0)
        {
            // No candidates is not a reason to ask the database which of nothing is live, and
            // `IN ()` is not valid SQL in the first place.
            return [];
        }

        // The direction of the question is the bound: the result cannot be larger than the page the
        // caller already holds, whatever the size of the table. Asking "give me every live key" instead
        // would load one column of every row in the system into the memory of a sweep that only ever
        // compares it against 500 candidates. The unique index on ObjectKey serves the probe.
        return await context.StoredFiles
            .AsNoTracking()
            .Where(file => candidateObjectKeys.Contains(file.ObjectKey))
            .Select(file => file.ObjectKey)
            .ToListAsync(cancellationToken);
    }

    private static IQueryable<StoredFileRecord> ApplyFilter(
        IQueryable<StoredFileRecord> source,
        StoredFileFilter filter)
    {
        if (filter.Search is { } search)
        {
            // The name only. The object key is not searchable and must not become so: it addresses
            // bytes, and a caller able to probe for keys is a caller able to probe for other people's
            // objects.
            string pattern = StoredFileLikePattern.Contains(search.Value);
            source = source.Where(file => EF.Functions.ILike(file.Name, pattern, "\\"));
        }

        if (filter.State is { } state)
        {
            source = source.Where(file => file.State == state);
        }

        return source;
    }

    private static async Task<PagedResult<StoredFileDto>> GetOffsetPageAsync(
        IQueryable<StoredFileRecord> filtered,
        StoredFilePageRequest request,
        CancellationToken cancellationToken)
    {
        // Counted server-side. Materialising the page and calling Count() on the result would report
        // the page's size, not the total, which is the classic pagination bug.
        int totalCount = await filtered.CountAsync(cancellationToken);

        int page = request.Paging.Page!.Value;
        int pageSize = request.Paging.PageSize;

        var items = await StoredFileSortMap.ApplyOrder(filtered, request.Sort)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(_toDto)
            .ToListAsync(cancellationToken);

        return PagedResult.Offset(items, page, pageSize, totalCount);
    }

    private static async Task<PagedResult<StoredFileDto>> GetKeysetPageAsync(
        IQueryable<StoredFileRecord> filtered,
        StoredFilePageRequest request,
        CancellationToken cancellationToken)
    {
        // Cursor mode never carries more than one sort term — GetStoredFilesRequestBinder already
        // refuses a multi-term sort under paging=cursor — so this is always the one term to compare
        // against.
        var term = request.Sort.Terms[0];
        int pageSize = request.Paging.PageSize;

        var keysetSource = request.Paging.Cursor is { } cursor
            ? StoredFileSortMap.ApplyKeyset(filtered, term, cursor)
            : filtered;

        // One extra row is how "is there a next page" is answered without a second query.
        var items = await StoredFileSortMap.ApplyOrder(keysetSource, request.Sort)
            .Take(pageSize + 1)
            .Select(_toDto)
            .ToListAsync(cancellationToken);

        bool hasNext = items.Count > pageSize;
        var page = hasNext ? items.GetRange(0, pageSize) : items;

        string? nextCursor = null;

        if (hasNext)
        {
            // The cursor names the last row this page actually served, read off the projection —
            // nothing is materialised to produce it.
            var last = page[^1];

            nextCursor = Cursor.After(term, StoredFileSortMap.KeyOf(last, term.Field), last.Id).Encode();
        }

        return PagedResult.Keyset(page, pageSize, nextCursor);
    }

    /// <remarks>
    /// One shape for every read, because the DTO has one — see <see cref="StoredFileDto"/> for why a
    /// flat aggregate gets no summary/detail split. It carries no object key: that value addresses the
    /// bytes and is the store's business, not the client's.
    /// </remarks>
    private static readonly Expression<Func<StoredFileRecord, StoredFileDto>> _toDto =
        file => new StoredFileDto(
            file.Id,
            file.Name,
            file.DeclaredMediaType,
            file.SizeInBytes,
            file.Checksum,
            file.State,
            file.RegisteredAt,
            file.AvailableAt);
}

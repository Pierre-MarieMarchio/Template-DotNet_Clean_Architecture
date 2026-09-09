using System.Linq.Expressions;
using AppTemplate.Application.Core.Common.Collections;
using AppTemplate.Application.Core.Common.Concurrency;
using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Application.Features.Files.Dtos;
using AppTemplate.Application.Features.Files.Ports.StoredFileQueries;
using AppTemplate.Domain.Core.Common.Primitives;
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
        UserId ownerId,
        FeaturePageRequest<StoredFileFilter> request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var owned = context.StoredFiles
            .AsNoTracking()
            .Where(file => file.OwnerId == ownerId.Value);

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
        UserId ownerId,
        CancellationToken cancellationToken = default) =>
        context.StoredFiles
            .AsNoTracking()
            // One query, so a missing file and someone else's file are indistinguishable. Fetching
            // by id and comparing the owner afterwards would read another user's row first.
            .Where(file => file.Id == id && file.OwnerId == ownerId.Value)
            .Select(file => new Versioned<StoredFileDto>(
                new StoredFileDto(
                    file.Id,
                    file.Name,
                    file.DeclaredMediaType,
                    file.SizeInBytes,
                    file.Checksum,
                    file.State,
                    file.RegisteredAt,
                    file.AvailableAt,
                    file.Tags.Select(tag => tag.Value).ToList()),
                file.Version))
            .FirstOrDefaultAsync(cancellationToken);


    public async Task<OwnerStorageUsage> GetUsageForOwnerAsync(
        UserId ownerId,
        CancellationToken cancellationToken = default)
    {
        var totals = await context.StoredFiles
            .AsNoTracking()
            .Where(file => file.OwnerId == ownerId.Value)
            .GroupBy(file => file.State)
            .Select(group => new
            {
                State = group.Key,
                Count = group.Count(),
                Bytes = group.Sum(file => file.SizeInBytes),
            })
            .ToListAsync(cancellationToken);

        // "not Pending" rather than a list of the other three, so a state added to the enum counts
        // against the quota from the day it is added.
        var stored = totals.Where(total => total.State != StoredFileState.Pending).ToList();
        var pending = totals.FirstOrDefault(total => total.State == StoredFileState.Pending);

        // An owner with no file of a state has no row for it, hence the defaults.
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
            // IN () is not valid SQL.
            return [];
        }

        // Bounded by the candidates the caller already holds. Asking for every live key instead
        // would load one column of every row in the system. The unique index on ObjectKey serves it.
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
            // The name only. A searchable object key would let a caller probe for other people's
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
        FeaturePageRequest<StoredFileFilter> request,
        CancellationToken cancellationToken)
    {
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
        FeaturePageRequest<StoredFileFilter> request,
        CancellationToken cancellationToken)
    {
        // GetStoredFilesRequestBinder refuses a multi-term sort under paging=cursor, so there is
        // always exactly one term here.
        var term = request.Sort.Terms[0];
        int pageSize = request.Paging.PageSize;

        var keysetSource = request.Paging.Cursor is { } cursor
            ? StoredFileSortMap.ApplyKeyset(filtered, term, cursor)
            : filtered;

        // One row beyond the page answers "is there a next page" without a second query.
        var items = await StoredFileSortMap.ApplyOrder(keysetSource, request.Sort)
            .Take(pageSize + 1)
            .Select(_toDto)
            .ToListAsync(cancellationToken);

        bool hasNext = items.Count > pageSize;
        var page = hasNext ? items.GetRange(0, pageSize) : items;

        string? nextCursor = null;

        if (hasNext)
        {
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
            file.AvailableAt,
            file.Tags.Select(tag => tag.Value).ToList());
}

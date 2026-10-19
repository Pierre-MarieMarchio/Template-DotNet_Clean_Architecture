using System.Linq.Expressions;
using AppTemplate.Application.Core.Common.Collections;
using AppTemplate.Application.Core.Common.Concurrency;
using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Application.Features.Files.Model.Dtos;
using AppTemplate.Application.Features.Files.Ports.StoredFileQueries;
using AppTemplate.Domain.Core.Common.Primitives;
using AppTemplate.Domain.Features.Files.Model.ValueObjects;
using AppTemplate.Infrastructure.Persistence.Common.Contexts;
using AppTemplate.Infrastructure.Persistence.Features.Files.Models;
using Microsoft.EntityFrameworkCore;

namespace AppTemplate.Infrastructure.Persistence.Features.Files.Queries;

/// <summary>
/// The read side: every method projects straight into a DTO or a scalar, never materialising or
/// tracking an entity, and never writes.
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

    /// <remarks>The version is selected by the same statement, so it describes exactly the row read.</remarks>
    public Task<Versioned<StoredFileDto>?> GetDetailAsync(
        Guid id,
        UserId ownerId,
        CancellationToken cancellationToken = default) =>
        context.StoredFiles
            .AsNoTracking()
            // One query, so a missing file and someone else's are indistinguishable.
            .Where(file => file.Id == id && file.OwnerId == ownerId.Value)
            .Select(file => new Versioned<StoredFileDto>(
                new StoredFileDto(
                    file.Id,
                    file.Category,
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
        FileCategory category,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(category);

        var totals = await context.StoredFiles
            .AsNoTracking()
            .Where(file => file.OwnerId == ownerId.Value && file.Category == category.Value)
            .GroupBy(file => file.State)
            .Select(group => new
            {
                State = group.Key,
                Count = group.Count(),
                Bytes = group.Sum(file => file.SizeInBytes),
            })
            .ToListAsync(cancellationToken);

        // "Not Pending" rather than a list, so a new state counts against the quota from the start.
        var stored = totals.Where(total => total.State != StoredFileState.Pending).ToList();
        var pending = totals.FirstOrDefault(total => total.State == StoredFileState.Pending);

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

        // Bounded by the caller's candidates and served by the unique index on ObjectKey.
        return await context.StoredFiles
            .AsNoTracking()
            .Where(file => candidateObjectKeys.Contains(file.ObjectKey))
            .Select(file => file.ObjectKey)
            .ToListAsync(cancellationToken);
    }

    /// <remarks>
    /// The one query here that crosses owners on purpose: it exists so a housekeeping pass can ask
    /// which ids still hold data, and returns ids only. The primitive is built client-side, because
    /// its factory validates and validation is not SQL.
    /// </remarks>
    public async Task<IReadOnlyList<UserId>> ListDataOwnerIdsAsync(
        CancellationToken cancellationToken = default)
    {
        var ids = await context.StoredFiles
            .AsNoTracking()
            .Select(file => file.OwnerId)
            .Distinct()
            .ToListAsync(cancellationToken);

        return [.. ids.Select(UserId.Create)];
    }

    private static IQueryable<StoredFileRecord> ApplyFilter(
        IQueryable<StoredFileRecord> source,
        StoredFileFilter filter)
    {
        if (filter.Search is { } search)
        {
            // The name only: a searchable object key would let a caller probe for others' objects.
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
        // The binder refuses a multi-term sort under paging=cursor: exactly one term here.
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

    /// <remarks>One shape for every read. It carries no object key: that addresses the bytes and is not the client's business.</remarks>
    private static readonly Expression<Func<StoredFileRecord, StoredFileDto>> _toDto =
        file => new StoredFileDto(
            file.Id,
            file.Category,
            file.Name,
            file.DeclaredMediaType,
            file.SizeInBytes,
            file.Checksum,
            file.State,
            file.RegisteredAt,
            file.AvailableAt,
            file.Tags.Select(tag => tag.Value).ToList());
}

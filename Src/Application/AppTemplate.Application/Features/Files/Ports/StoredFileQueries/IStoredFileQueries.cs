using AppTemplate.Application.Core.Common.Collections;
using AppTemplate.Application.Core.Common.Concurrency;
using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Application.Features.Files.Dtos;
using AppTemplate.Application.Features.Files.Policies;
using AppTemplate.Domain.Core.Common.Primitives;
using AppTemplate.Domain.Features.Files.Repositories;
using AppTemplate.Domain.Features.Files.ValueObjects;

namespace AppTemplate.Application.Features.Files.Ports.StoredFileQueries;

/// <summary>
/// The read side of the Files feature: projections to DTOs and columns, which <see cref="IStoredFileRepository"/>
/// does not serve.
/// </summary>
public interface IStoredFileQueries
{
    /// <summary>
    /// The owner's files. <paramref name="request"/> has been through <see cref="StoredFileCollectionPolicy"/>, so
    /// paging, sort and filter are only translated here.
    /// </summary>
    Task<PagedResult<StoredFileDto>> GetForOwnerAsync(
        UserId ownerId,
        FeaturePageRequest<StoredFileFilter> request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// The file and its version, or null when it does not exist or belongs to someone else; the two are
    /// indistinguishable so a caller cannot probe for ids.
    /// </summary>
    Task<Versioned<StoredFileDto>?> GetDetailAsync(
        Guid id,
        UserId ownerId,
        CancellationToken cancellationToken = default);

    /// <summary>One owner's totals in one category, counted in the database.</summary>
    Task<OwnerStorageUsage> GetUsageForOwnerAsync(
        UserId ownerId,
        FileCategory category,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Which of <paramref name="candidateObjectKeys"/> some row still names: the orphan sweep's half of the
    /// difference. Asked as "which of these are live?" so the answer is bounded by the page the caller holds,
    /// whatever the table's size. Unordered.
    /// </summary>
    Task<IReadOnlyList<string>> GetLiveObjectKeysAsync(
        IReadOnlyList<string> candidateObjectKeys,
        CancellationToken cancellationToken = default);
}

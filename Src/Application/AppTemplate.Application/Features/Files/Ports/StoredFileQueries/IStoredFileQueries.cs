using AppTemplate.Application.Common.Concurrency;
using AppTemplate.Application.Common.Results;
using AppTemplate.Application.Features.Files.Dtos;
using AppTemplate.Application.Features.Files.Policies;
using AppTemplate.Domain.Features.Files.Repositories;

namespace AppTemplate.Application.Features.Files.Ports.StoredFileQueries;

/// <summary>
/// The read side of the file feature. Separate from <see cref="IStoredFileRepository"/> for the
/// usual reason — reads project straight to DTOs with no change tracking — and because two of the
/// four answers below are columns rather than aggregates, which is exactly what the repository's own
/// documentation says it will not pretend to serve.
/// </summary>
public interface IStoredFileQueries
{
    /// <summary>
    /// <paramref name="request"/> has already been through
    /// <see cref="StoredFileCollectionPolicy"/>'s whitelist, so nothing here re-validates paging,
    /// sort or filter — it only translates them.
    /// </summary>
    Task<PagedResult<StoredFileDto>> GetForOwnerAsync(
        Guid ownerId,
        StoredFilePageRequest request,
        CancellationToken cancellationToken = default);

    /// <returns>The file and the aggregate's version, or <c>null</c> when it does not exist or is
    /// not owned by <paramref name="ownerId"/> — the two are deliberately indistinguishable, so a
    /// caller cannot use this to probe for other users' file ids.</returns>
    Task<Versioned<StoredFileDto>?> GetDetailAsync(
        Guid id,
        Guid ownerId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// One owner's totals, counted in the database rather than by loading their files. A quota check
    /// that materialised every aggregate to add up four numbers would cost more than the upload it
    /// is guarding.
    /// </summary>
    Task<OwnerStorageUsage> GetUsageForOwnerAsync(Guid ownerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Which of <paramref name="candidateObjectKeys"/> some row still names. The orphan sweep's half
    /// of the difference: it holds a page of keys the store reported and needs to know which of them
    /// are still owed, and this answers in one round trip per page instead of one per key.
    /// <para>
    /// Asked in this direction — "which of these are live?" rather than "give me every live key" —
    /// because the answer is then bounded by the page the caller already holds, whatever the size of
    /// the table behind it.
    /// </para>
    /// </summary>
    /// <returns>The subset that exists, in no guaranteed order. A key absent from the result is
    /// named by no row.</returns>
    Task<IReadOnlyList<string>> GetLiveObjectKeysAsync(
        IReadOnlyList<string> candidateObjectKeys,
        CancellationToken cancellationToken = default);
}

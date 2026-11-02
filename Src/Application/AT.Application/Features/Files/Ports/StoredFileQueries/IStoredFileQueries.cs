using AppTemplate.Application.Core.Common.Collections;
using AppTemplate.Application.Core.Common.Concurrency;
using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Application.Features.Files.Model.Dtos;
using AppTemplate.Domain.Core.Common.Primitives;
using AppTemplate.Domain.Features.Files.Model.ValueObjects;

namespace AppTemplate.Application.Features.Files.Ports.StoredFileQueries;

public interface IStoredFileQueries
{
    Task<PagedResult<StoredFileDto>> GetForOwnerAsync(
        UserId ownerId,
        FeaturePageRequest<StoredFileFilter> request,
        CancellationToken cancellationToken = default);

    Task<Versioned<StoredFileDto>?> GetDetailAsync(
        Guid id,
        UserId ownerId,
        CancellationToken cancellationToken = default);

    Task<OwnerStorageUsage> GetUsageForOwnerAsync(
        UserId ownerId,
        FileCategory category,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<string>> GetLiveObjectKeysAsync(
        IReadOnlyList<string> candidateObjectKeys,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<UserId>> ListDataOwnerIdsAsync(CancellationToken cancellationToken = default);
}

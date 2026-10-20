using AppTemplate.Application.Core.Common.Concurrency;
using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Domain.Features.Files.Model.Entities;

namespace AppTemplate.Application.Features.Files.Services;

public interface IStoredFileService
{
    Task<Result<StoredFile>> LoadOwnedAsync(
        Guid storedFileId,
        VersionPrecondition? precondition,
        CancellationToken cancellationToken = default);

    Task<Result<StoredFile>> LoadReadableAsync(Guid storedFileId, CancellationToken cancellationToken = default);
}

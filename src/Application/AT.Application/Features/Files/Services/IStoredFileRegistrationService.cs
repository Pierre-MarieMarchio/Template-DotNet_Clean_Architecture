using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Domain.Core.Common.Primitives;
using AppTemplate.Domain.Features.Files.Model.Entities;
using AppTemplate.Domain.Features.Files.Model.ValueObjects;

namespace AppTemplate.Application.Features.Files.Services;

public interface IStoredFileRegistrationService
{
    Task<Result<StoredFile>> RegisterAsync(
        UserId owner,
        FileCategory category,
        string name,
        string mediaType,
        long sizeInBytes,
        string checksum,
        CancellationToken cancellationToken = default);

    Task WithdrawAsync(StoredFile storedFile, CancellationToken cancellationToken = default);
}

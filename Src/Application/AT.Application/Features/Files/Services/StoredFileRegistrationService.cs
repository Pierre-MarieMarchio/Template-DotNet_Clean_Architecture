using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Application.Core.Common.Validation;
using AppTemplate.Application.Features.Files.Configuration.Options;
using AppTemplate.Application.Features.Files.Model.Errors;
using AppTemplate.Application.Features.Files.Model.Policies;
using AppTemplate.Application.Features.Files.Ports.OwnerQuotaLock;
using AppTemplate.Application.Features.Files.Ports.StoredFileQueries;
using AppTemplate.Domain.Core.Common.Primitives;
using AppTemplate.Domain.Features.Files.Model.Entities;
using AppTemplate.Domain.Features.Files.Model.ValueObjects;
using AppTemplate.Domain.Features.Files.Repositories;
using Microsoft.Extensions.Options;

namespace AppTemplate.Application.Features.Files.Services;

internal sealed class StoredFileRegistrationService(
    IStoredFileRepository repository,
    IStoredFileQueries queries,
    IOwnerQuotaLock quotaLock,
    IUnitOfWork unitOfWork,
    IDateTimeProvider dateTimeProvider,
    IOptions<FilesOptions> options) : IStoredFileRegistrationService
{
    public async Task<Result<StoredFile>> RegisterAsync(
        UserId owner,
        FileCategory category,
        string name,
        string mediaType,
        long sizeInBytes,
        string checksum,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(category);

        var quota = (options.Value.Find(category)
            ?? throw new InvalidOperationException($"Category '{category.Value}' is not declared.")).Quota;

        async Task<Result<StoredFile>> CheckQuotaAndCommitAsync(CancellationToken token)
        {
            var usage = await queries.GetUsageForOwnerAsync(owner, category, token);
            var room = StoredFileQuotaPolicy.EnsureRoomFor(usage, quota, sizeInBytes);

            if (room.IsFailure)
            {
                return room.To<StoredFile>();
            }

            var registration = DomainGuard.Try(() => StoredFile.Register(
                owner,
                category,
                StoredFileDeclaration.Create(
                    StoredFileName.Create(name),
                    DeclaredMediaType.Create(mediaType),
                    FileSize.Create(sizeInBytes),
                    Sha256Checksum.Create(checksum)),
                dateTimeProvider.UtcNow));

            if (registration.IsFailure)
            {
                return registration;
            }

            repository.Add(registration.Value);
            await unitOfWork.SaveChangesAsync(token);

            return registration;
        }

        if (!quota.IsStrict)
        {
            return await CheckQuotaAndCommitAsync(cancellationToken);
        }

        try
        {
            return await quotaLock.RunExclusivelyAsync(owner, category, CheckQuotaAndCommitAsync, cancellationToken);
        }
        catch (OwnerQuotaLockTimeoutException)
        {
            return Result.Failure<StoredFile>(StoredFileErrors.QuotaBusy(category.Value));
        }
    }

    public async Task WithdrawAsync(StoredFile storedFile, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(storedFile);

        storedFile.Delete(dateTimeProvider.UtcNow);
        repository.Remove(storedFile);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

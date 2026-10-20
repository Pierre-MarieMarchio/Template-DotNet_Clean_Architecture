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

        Task<Result<StoredFile>> Register(CancellationToken token) =>
            CheckQuotaAndCommitAsync(owner, category, quota, name, mediaType, sizeInBytes, checksum, token);

        if (!quota.IsStrict)
        {
            return await Register(cancellationToken);
        }

        try
        {
            return await quotaLock.RunExclusivelyAsync(owner, category, Register, cancellationToken);
        }
        catch (OwnerQuotaLockTimeoutException)
        {
            return Result.Failure<StoredFile>(StoredFileErrors.QuotaBusy(category.Value));
        }
    }

    private async Task<Result<StoredFile>> CheckQuotaAndCommitAsync(
        UserId owner,
        FileCategory category,
        FileCategoryQuotaOptions quota,
        string name,
        string mediaType,
        long sizeInBytes,
        string checksum,
        CancellationToken cancellationToken)
    {
        var usage = await queries.GetUsageForOwnerAsync(owner, category, cancellationToken);
        var room = StoredFileQuotaPolicy.EnsureRoomFor(usage, quota, sizeInBytes);

        if (room.IsFailure)
        {
            return room.To<StoredFile>();
        }

        var registration = DomainGuard.Try(() => StoredFile.Register(
            owner,
            category,
            StoredFileName.Create(name),
            DeclaredMediaType.Create(mediaType),
            FileSize.Create(sizeInBytes),
            Sha256Checksum.Create(checksum),
            dateTimeProvider.UtcNow));

        if (registration.IsFailure)
        {
            return registration;
        }

        repository.Add(registration.Value);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return registration;
    }
}

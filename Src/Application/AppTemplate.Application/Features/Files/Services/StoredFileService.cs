using AppTemplate.Application.Core.Common.Concurrency;
using AppTemplate.Application.Core.Common.Ownership;
using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Application.Features.Files.Configuration.Options;
using AppTemplate.Application.Features.Files.Model.Errors;
using AppTemplate.Domain.Features.Files.Model.Entities;
using AppTemplate.Domain.Features.Files.Model.ValueObjects;
using AppTemplate.Domain.Features.Files.Repositories;
using Microsoft.Extensions.Options;

namespace AppTemplate.Application.Features.Files.Services;

internal sealed class StoredFileService(
    IStoredFileRepository repository,
    ICurrentUser currentUser,
    IOptions<FilesOptions> options) : IStoredFileService
{
    public async Task<Result<StoredFile>> LoadOwnedAsync(
        Guid storedFileId,
        VersionPrecondition? precondition,
        CancellationToken cancellationToken = default)
    {
        var userId = currentUser.RequireUserId();

        if (userId.IsFailure)
        {
            return userId.To<StoredFile>();
        }

        return OwnedAggregate.Require(
            await repository.GetAsync(storedFileId, cancellationToken),
            userId.Value,
            StoredFileErrors.FileNotFound(storedFileId),
            precondition);
    }

    public async Task<Result<StoredFile>> LoadReadableAsync(
        Guid storedFileId,
        CancellationToken cancellationToken = default)
    {
        var userId = currentUser.RequireUserId();

        if (userId.IsFailure)
        {
            return userId.To<StoredFile>();
        }

        var storedFile = await repository.GetAsync(storedFileId, cancellationToken);

        if (storedFile is not null
            && (storedFile.OwnerId == userId.Value || IsOpenToEverySignedInUser(storedFile)))
        {
            return storedFile;
        }

        return Result.Failure<StoredFile>(StoredFileErrors.FileNotFound(storedFileId));
    }

    private bool IsOpenToEverySignedInUser(StoredFile storedFile) =>
        storedFile.State == StoredFileState.Available
        && options.Value.Find(storedFile.Category)?.IsReadableByAnySignedInUser == true;
}

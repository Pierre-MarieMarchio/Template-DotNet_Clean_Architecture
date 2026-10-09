using AppTemplate.Application.Core.Common.Concurrency;
using AppTemplate.Application.Core.Common.Ownership;
using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Application.Features.Files.Errors;
using AppTemplate.Application.Features.Files.Options;
using AppTemplate.Domain.Features.Files.Entities;
using AppTemplate.Domain.Features.Files.Repositories;
using AppTemplate.Domain.Features.Files.ValueObjects;
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

        // A file's bytes are addressed by a key nobody can guess, but its id travels in a URL, which
        // is why answering a non-owner as absent matters here in particular.
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

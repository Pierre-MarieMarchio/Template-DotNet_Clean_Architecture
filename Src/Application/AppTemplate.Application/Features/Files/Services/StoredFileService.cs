using AppTemplate.Application.Common.Concurrency;
using AppTemplate.Application.Common.Ports;
using AppTemplate.Application.Common.Results;
using AppTemplate.Application.Features.Files.Errors;
using AppTemplate.Domain.Features.Files.Entities;
using AppTemplate.Domain.Features.Files.Repositories;

namespace AppTemplate.Application.Features.Files.Services;

internal sealed class StoredFileService(IStoredFileRepository repository, ICurrentUser currentUser) : IStoredFileService
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

        var ownerId = userId.Value;

        var storedFile = await repository.GetAsync(storedFileId, cancellationToken);

        // One answer for "no such file" and "not yours". A file's bytes are addressed by a key
        // nobody can guess, but its id travels in a URL, so telling the two apart would turn this
        // endpoint into a way of asking whether a given id belongs to somebody.
        if (storedFile is null || storedFile.OwnerId != ownerId)
        {
            return Result.Failure<StoredFile>(StoredFileErrors.FileNotFound(storedFileId));
        }

        // Compared against the aggregate this call just loaded, so nothing can commit between the
        // comparison and whatever the caller does with the result.
        if (precondition is not null && !precondition.IsSatisfiedBy(storedFile.Version))
        {
            return Result.Failure<StoredFile>(ConcurrencyErrors.PreconditionFailed);
        }

        return storedFile;
    }
}

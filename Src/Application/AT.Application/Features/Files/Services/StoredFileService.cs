using AppTemplate.Application.Core.Common.Concurrency;
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

        var aggregate = await repository.GetAsync(userId.Value, storedFileId, cancellationToken);

        if (aggregate is null)
        {
            return Result.Failure<StoredFile>(StoredFileErrors.FileNotFound(storedFileId));
        }

        return precondition is not null && !precondition.IsSatisfiedBy(aggregate.Version)
            ? Result.Failure<StoredFile>(ConcurrencyErrors.PreconditionFailed)
            : Result.Success(aggregate);
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

        var storedFile = await repository.GetReadableAsync(
            userId.Value,
            storedFileId,
            CategoriesOpenToEverySignedInUser(),
            cancellationToken);

        return storedFile is null
            ? Result.Failure<StoredFile>(StoredFileErrors.FileNotFound(storedFileId))
            : Result.Success(storedFile);
    }

    private List<FileCategory> CategoriesOpenToEverySignedInUser() =>
        options.Value.Categories
            .Where(category => category.Value.IsReadableByAnySignedInUser)
            .Select(category => FileCategory.Create(category.Key))
            .ToList();
}

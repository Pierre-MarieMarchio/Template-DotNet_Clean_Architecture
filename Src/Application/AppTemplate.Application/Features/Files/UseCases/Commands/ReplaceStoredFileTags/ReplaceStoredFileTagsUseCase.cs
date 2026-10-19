using AppTemplate.Application.Common.Tagging;
using AppTemplate.Application.Core.Common.Concurrency;
using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Application.Core.Common.Validation;
using AppTemplate.Application.Features.Files.Model.Dtos;
using AppTemplate.Application.Features.Files.Model.Mapping;
using AppTemplate.Application.Features.Files.Services;
using FluentValidation;

namespace AppTemplate.Application.Features.Files.UseCases.Commands.ReplaceStoredFileTags;

/// <summary>
/// Labels a file: loads, delegates to the domain's <c>TagSet</c> rules and commits.
/// </summary>
public sealed class ReplaceStoredFileTagsUseCase(
    IStoredFileService files,
    IUnitOfWork unitOfWork,
    ICacheStore cache,
    IValidator<ReplaceStoredFileTagsCommand> validator) : IReplaceStoredFileTagsUseCase
{
    public async Task<Result<Versioned<StoredFileDto>>> ExecuteAsync(
        ReplaceStoredFileTagsCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var validation = await validator.EnsureValidAsync(command, cancellationToken);

        if (validation.IsFailure)
        {
            return validation.To<Versioned<StoredFileDto>>();
        }

        var access = await files.LoadOwnedAsync(command.StoredFileId, command.Precondition, cancellationToken);

        if (access.IsFailure)
        {
            return access.To<Versioned<StoredFileDto>>();
        }

        var file = access.Value;

        // Caught: the domain refuses a set over the cap, the second line behind the validator.
        var replacement = DomainGuard.Try(() => file.SetTags(command.Tags));

        if (replacement.IsFailure)
        {
            return replacement.To<Versioned<StoredFileDto>>();
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        await cache.RemoveAsync(
            UsedTagsCache.KeyFor(UsedTagsCache.FileScope, file.OwnerId),
            cancellationToken);

        return StoredFileDtoMapping.ToVersioned(file);
    }
}

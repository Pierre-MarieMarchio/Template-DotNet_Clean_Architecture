using AppTemplate.Application.Core.Common.Concurrency;
using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Application.Core.Common.Validation;
using AppTemplate.Application.Features.Files.Dtos;
using AppTemplate.Application.Features.Files.Mapping;
using AppTemplate.Application.Features.Files.Services;
using FluentValidation;

namespace AppTemplate.Application.Features.Files.UseCases.Commands.ReplaceStoredFileTags;

/// <summary>
/// Labels a file. The rules the set obeys are the domain's and are shared with the to-do item —
/// <c>TagSet</c> — so this use case loads, delegates and commits.
/// </summary>
public sealed class ReplaceStoredFileTagsUseCase(
    IStoredFileService files,
    IUnitOfWork unitOfWork,
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

        // Caught: a replacement larger than the cap is refused by the domain, and the validator's
        // count rule reads the same constant — so this is the second line of the same defence
        // rather than a case the first one misses.
        var replacement = DomainGuard.Try(() => file.SetTags(command.Tags));

        if (replacement.IsFailure)
        {
            return replacement.To<Versioned<StoredFileDto>>();
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return StoredFileDtoMapping.ToVersioned(file);
    }
}

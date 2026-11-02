using AppTemplate.Application.Common.Tagging;
using AppTemplate.Application.Core.Common.Concurrency;
using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Application.Core.Common.Validation;
using AppTemplate.Application.Features.Files.Model.Dtos;
using AppTemplate.Application.Features.Files.Model.Mapping;
using AppTemplate.Application.Features.Files.Services;
using Mediator;

namespace AppTemplate.Application.Features.Files.UseCases.Commands.ReplaceStoredFileTags;

public sealed record ReplaceStoredFileTagsCommand(
    Guid StoredFileId,
    IReadOnlyList<string> Tags,
    VersionPrecondition? Precondition = null) : ICommand<Result<Versioned<StoredFileDto>>>;

public sealed class ReplaceStoredFileTagsCommandHandler(
    IStoredFileService files,
    IUnitOfWork unitOfWork,
    ICacheStore cache) : ICommandHandler<ReplaceStoredFileTagsCommand, Result<Versioned<StoredFileDto>>>
{
    public async ValueTask<Result<Versioned<StoredFileDto>>> Handle(
        ReplaceStoredFileTagsCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var access = await files.LoadOwnedAsync(command.StoredFileId, command.Precondition, cancellationToken);

        if (access.IsFailure)
        {
            return access.To<Versioned<StoredFileDto>>();
        }

        var file = access.Value;

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

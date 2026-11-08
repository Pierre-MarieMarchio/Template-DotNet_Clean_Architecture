using AppTemplate.Application.Core.Common.Concurrency;
using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Application.Features.Files.Model.Dtos;
using AppTemplate.Application.Features.Files.Model.Mapping;
using AppTemplate.Application.Features.Files.Services;
using Mediator;

namespace AppTemplate.Application.Features.Files.UseCases.Commands.ConfirmFileUpload;

public sealed record ConfirmFileUploadCommand(Guid StoredFileId, VersionPrecondition? Precondition = null) : ICommand<Result<Versioned<StoredFileDto>>>;

public sealed class ConfirmFileUploadCommandHandler(
    IStoredFileService files,
    IStoredFileDepositService deposits) : ICommandHandler<ConfirmFileUploadCommand, Result<Versioned<StoredFileDto>>>
{
    public async ValueTask<Result<Versioned<StoredFileDto>>> Handle(
        ConfirmFileUploadCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var access = await files.LoadOwnedAsync(command.StoredFileId, command.Precondition, cancellationToken);

        if (access.IsFailure)
        {
            return access.To<Versioned<StoredFileDto>>();
        }

        var storedFile = access.Value;

        var confirmation = await deposits.ConfirmAsync(storedFile, cancellationToken);

        if (confirmation.IsFailure)
        {
            return confirmation.To<Versioned<StoredFileDto>>();
        }

        return StoredFileDtoMapping.ToVersioned(storedFile);
    }
}

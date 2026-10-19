using AppTemplate.Application.Core.Common.Concurrency;
using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Application.Core.Common.Validation;
using AppTemplate.Application.Features.Files.Model.Dtos;
using AppTemplate.Application.Features.Files.Model.Mapping;
using AppTemplate.Application.Features.Files.Services;
using AppTemplate.Domain.Features.Files.Model.ValueObjects;
using FluentValidation;

namespace AppTemplate.Application.Features.Files.UseCases.Commands.ConfirmFileUpload;

/// <summary>The second half of an upload: asks the store what is actually there and lets the
/// aggregate decide whether it is what was promised. The file comes out
/// <see cref="StoredFileState.Deposited"/>, not readable — <c>InspectDepositedFilesUseCase</c>
/// releases or refuses it. A mismatch is reported and nothing repaired: the file stays pending and
/// the abandonment sweep removes it. The description, transition and commit are
/// <see cref="IStoredFileDepositService"/>'s, shared with the API upload.</summary>
public sealed class ConfirmFileUploadUseCase(
    IStoredFileService files,
    IStoredFileDepositService deposits,
    IValidator<ConfirmFileUploadCommand> validator) : IConfirmFileUploadUseCase
{
    public async Task<Result<Versioned<StoredFileDto>>> ExecuteAsync(
        ConfirmFileUploadCommand command,
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

        var storedFile = access.Value;

        var confirmation = await deposits.ConfirmAsync(storedFile, cancellationToken);

        if (confirmation.IsFailure)
        {
            return confirmation.To<Versioned<StoredFileDto>>();
        }

        return StoredFileDtoMapping.ToVersioned(storedFile);
    }
}

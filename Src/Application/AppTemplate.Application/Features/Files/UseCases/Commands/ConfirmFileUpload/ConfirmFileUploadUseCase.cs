using AppTemplate.Application.Common.Abstractions;
using AppTemplate.Application.Common.Concurrency;
using AppTemplate.Application.Common.Results;
using AppTemplate.Application.Common.Validation;
using AppTemplate.Application.Features.Files.Dtos;
using AppTemplate.Application.Features.Files.Errors;
using AppTemplate.Application.Features.Files.Mapping;
using AppTemplate.Application.Features.Files.Ports.FileContentStore;
using AppTemplate.Application.Features.Files.Services;
using AppTemplate.Domain.Features.Files.Entities;
using AppTemplate.Domain.Features.Files.ValueObjects;
using FluentValidation;

namespace AppTemplate.Application.Features.Files.UseCases.Commands.ConfirmFileUpload;

/// <summary>
/// The second half of an upload: the client says it has finished, this asks the store what is
/// actually there, and the aggregate decides whether that is what was promised.
/// <para>
/// <b>It no longer makes the file readable, and the name says only what it does.</b> Confirming a
/// deposit establishes that the bytes arrived and are the ones that were declared; it establishes
/// nothing about what they <em>are</em>. The file comes out of here
/// <see cref="StoredFileState.Deposited"/>, and <c>InspectDepositedFilesUseCase</c> — which reads
/// the content, and therefore cannot run inside this request — is what releases or refuses it. A
/// client that needs to know reads the file's state back.
/// </para>
/// <para>
/// <b>A mismatch is reported and nothing is repaired.</b> <see cref="StoredFile.ConfirmDeposit"/>
/// refuses and leaves the file pending, which is the safe direction — nothing is served — and the
/// abandonment sweep removes the registration on its own schedule. So there is deliberately no
/// retry, no partial state and no compensating delete here: writing recovery for this would mean
/// deciding, on the client's behalf, whether the bytes it sent or the digest it declared was the
/// wrong one, and this process has read neither.
/// </para>
/// <para>
/// The store's report is turned into value objects inside the guard as well, because those factories
/// refuse too: a zero-byte object is what an interrupted deposit leaves behind, and
/// <c>FileSize</c> rejects it rather than letting an empty file be confirmed as a real one.
/// </para>
/// </summary>
public sealed class ConfirmFileUploadUseCase(
    IStoredFileAccess files,
    IFileContentStore content,
    IUnitOfWork unitOfWork,
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

        var description = await content.DescribeAsync(storedFile.ObjectKey.Value, cancellationToken);

        if (description is null)
        {
            return Result.Failure<Versioned<StoredFileDto>>(StoredFileErrors.DepositMissing(storedFile.Id));
        }

        var confirmation = DomainGuard.Try(() => storedFile.ConfirmDeposit(
            FileSize.Create(description.SizeInBytes),
            Sha256Checksum.Create(description.Checksum)));

        if (confirmation.IsFailure)
        {
            return confirmation.To<Versioned<StoredFileDto>>();
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return StoredFileDtoMapping.ToVersioned(storedFile);
    }
}

using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Application.Core.Common.Validation;
using AppTemplate.Application.Features.Files.Errors;
using AppTemplate.Application.Features.Files.Ports.FileContentStore;
using AppTemplate.Domain.Features.Files.Entities;
using AppTemplate.Domain.Features.Files.ValueObjects;

namespace AppTemplate.Application.Features.Files.Services;

internal sealed class StoredFileDepositService(
    IFileContentStore content,
    IUnitOfWork unitOfWork) : IStoredFileDepositService
{
    public async Task<Result> ConfirmAsync(StoredFile storedFile, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(storedFile);

        var description = await content.DescribeAsync(storedFile.ObjectKey.Value, cancellationToken);

        if (description is null)
        {
            return Result.Failure(StoredFileErrors.DepositMissing(storedFile.Id));
        }

        // Inside the guard too: FileSize refuses the zero-byte object an interrupted deposit leaves.
        var confirmation = DomainGuard.Try(() => storedFile.ConfirmDeposit(
            FileSize.Create(description.SizeInBytes),
            Sha256Checksum.Create(description.Checksum)));

        if (confirmation.IsFailure)
        {
            return confirmation;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}

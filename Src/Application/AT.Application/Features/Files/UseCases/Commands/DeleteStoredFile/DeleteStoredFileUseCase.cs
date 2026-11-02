using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Application.Core.Common.Validation;
using AppTemplate.Application.Features.Files.Services;
using AppTemplate.Domain.Features.Files.Repositories;
using FluentValidation;

namespace AppTemplate.Application.Features.Files.UseCases.Commands.DeleteStoredFile;

public sealed class DeleteStoredFileUseCase(
    IStoredFileService files,
    IStoredFileRepository repository,
    IUnitOfWork unitOfWork,
    IDateTimeProvider dateTimeProvider,
    IValidator<DeleteStoredFileCommand> validator) : IDeleteStoredFileUseCase
{
    public async Task<Result> ExecuteAsync(
        DeleteStoredFileCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var validation = await validator.EnsureValidAsync(command, cancellationToken);

        if (validation.IsFailure)
        {
            return validation;
        }

        var access = await files.LoadOwnedAsync(command.StoredFileId, command.Precondition, cancellationToken);

        if (access.IsFailure)
        {
            return access;
        }

        var storedFile = access.Value;

        var announcement = DomainGuard.Try(() => storedFile.Delete(dateTimeProvider.UtcNow));

        if (announcement.IsFailure)
        {
            return announcement;
        }

        repository.Remove(storedFile);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}

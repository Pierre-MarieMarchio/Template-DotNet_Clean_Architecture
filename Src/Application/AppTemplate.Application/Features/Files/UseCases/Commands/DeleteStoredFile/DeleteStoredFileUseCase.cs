using AppTemplate.Application.Common.Ports;
using AppTemplate.Application.Common.Results;
using AppTemplate.Application.Common.Validation;
using AppTemplate.Application.Features.Files.Services;
using AppTemplate.Domain.Features.Files.Repositories;
using FluentValidation;

namespace AppTemplate.Application.Features.Files.UseCases.Commands.DeleteStoredFile;

/// <summary>
/// Deleting a file is removing its row. There is no deleted state and no deletion instant — see
/// <c>CONTRIBUTING.md</c>'s "No soft delete" — so this use case has nothing to set and nothing to
/// project back.
/// <para>
/// <c>StoredFile.Delete</c> is called alongside the removal rather than instead of it. It writes
/// nothing; it raises the event that lets the bytes be reclaimed now instead of at the next sweep.
/// If that event is never delivered the file is still gone and its content is still reclaimed, by
/// the sweep that deletes what no row names — which is what makes it safe to have no outbox here.
/// </para>
/// </summary>
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

        // Caught: the aggregate refuses a second announcement in one unit of work, which depends on
        // what has already happened to this instance rather than on anything the caller sent.
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

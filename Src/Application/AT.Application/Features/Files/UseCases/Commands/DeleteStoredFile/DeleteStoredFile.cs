using AppTemplate.Application.Core.Common.Concurrency;
using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Application.Core.Common.Validation;
using AppTemplate.Application.Features.Files.Services;
using AppTemplate.Domain.Features.Files.Repositories;
using Mediator;

namespace AppTemplate.Application.Features.Files.UseCases.Commands.DeleteStoredFile;

public sealed record DeleteStoredFileCommand(Guid StoredFileId, VersionPrecondition? Precondition = null) : ICommand<Result>;

public sealed class DeleteStoredFileCommandHandler(
    IStoredFileService files,
    IStoredFileRepository repository,
    IUnitOfWork unitOfWork,
    IDateTimeProvider dateTimeProvider) : ICommandHandler<DeleteStoredFileCommand, Result>
{
    public async ValueTask<Result> Handle(
        DeleteStoredFileCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

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

using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Application.Features.Files.Configuration.Options;
using AppTemplate.Application.Features.Files.Model.Errors;
using AppTemplate.Application.Features.Files.Ports.FileDirectTransfer;
using AppTemplate.Application.Features.Files.Services;
using AppTemplate.Domain.Features.Files.Model.ValueObjects;
using Mediator;
using Microsoft.Extensions.Options;

namespace AppTemplate.Application.Features.Files.UseCases.Commands.RegisterFile;

public sealed record RegisterFileCommand(
    string Category,
    string Name,
    string MediaType,
    long SizeInBytes,
    string Checksum) : ICommand<Result<RegisterFileOutcome>>;

public sealed class RegisterFileCommandHandler(
    IStoredFileRegistrationService registrations,
    IFileDirectTransfer transfer,
    ICurrentUser currentUser,
    IOptions<FilesOptions> options) : ICommandHandler<RegisterFileCommand, Result<RegisterFileOutcome>>
{
    private static readonly TimeSpan _uploadWindow = TimeSpan.FromMinutes(30);

    public async ValueTask<Result<RegisterFileOutcome>> Handle(
        RegisterFileCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var userId = currentUser.RequireUserId();

        if (userId.IsFailure)
        {
            return userId.To<RegisterFileOutcome>();
        }

        var category = FileCategory.Create(command.Category);
        var categoryOptions = options.Value.Find(category)!;

        if (!categoryOptions.AllowsDirectUpload)
        {
            return Result.Failure<RegisterFileOutcome>(StoredFileErrors.DirectUploadNotAllowed(category.Value));
        }

        if (!transfer.IsSupported)
        {
            return Result.Failure<RegisterFileOutcome>(StoredFileErrors.DirectUploadUnavailable());
        }

        var registration = await registrations.RegisterAsync(
            userId.Value,
            category,
            command.Name,
            command.MediaType,
            command.SizeInBytes,
            command.Checksum,
            cancellationToken);

        if (registration.IsFailure)
        {
            return registration.To<RegisterFileOutcome>();
        }

        var storedFile = registration.Value;

        var grant = await transfer.CreateUploadGrantAsync(
            storedFile.ObjectKey.Value,
            storedFile.DeclaredMediaType.Value,
            storedFile.Size.Bytes,

            storedFile.Checksum.Value,
            _uploadWindow,
            cancellationToken);

        return new RegisterFileOutcome(storedFile.Id, grant);
    }
}

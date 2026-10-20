using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Application.Core.Common.Validation;
using AppTemplate.Application.Features.Files.Configuration.Options;
using AppTemplate.Application.Features.Files.Model.Errors;
using AppTemplate.Application.Features.Files.Ports.FileDirectTransfer;
using AppTemplate.Application.Features.Files.Services;
using AppTemplate.Domain.Features.Files.Model.ValueObjects;
using FluentValidation;
using Microsoft.Extensions.Options;

namespace AppTemplate.Application.Features.Files.UseCases.Commands.RegisterFile;

public sealed class RegisterFileUseCase(
    IStoredFileRegistrationService registrations,
    IFileDirectTransfer transfer,
    ICurrentUser currentUser,
    IOptions<FilesOptions> options,
    IValidator<RegisterFileCommand> validator) : IRegisterFileUseCase
{
    private static readonly TimeSpan _uploadWindow = TimeSpan.FromMinutes(30);

    public async Task<Result<RegisterFileOutcome>> ExecuteAsync(
        RegisterFileCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var userId = currentUser.RequireUserId();

        if (userId.IsFailure)
        {
            return userId.To<RegisterFileOutcome>();
        }

        var validation = await validator.EnsureValidAsync(command, cancellationToken);

        if (validation.IsFailure)
        {
            return validation.To<RegisterFileOutcome>();
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

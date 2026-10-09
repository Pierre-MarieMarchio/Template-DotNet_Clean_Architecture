using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Application.Core.Common.Validation;
using AppTemplate.Application.Features.Files.Errors;
using AppTemplate.Application.Features.Files.Options;
using AppTemplate.Application.Features.Files.Ports.FileDirectTransfer;
using AppTemplate.Application.Features.Files.Services;
using AppTemplate.Domain.Features.Files.ValueObjects;
using FluentValidation;
using Microsoft.Extensions.Options;

namespace AppTemplate.Application.Features.Files.UseCases.Commands.RegisterFile;

/// <summary>
/// Reserves a place for a file and hands back the right to deposit its bytes: the first half of an
/// upload, <c>ConfirmFileUploadUseCase</c> is the second.
/// <para>
/// The row is committed before the grant is minted: a grant issued first would authorise bytes
/// against a key no row names if the commit failed, whereas the worst case in this order is a
/// pending row the abandonment sweep removes. Everything that can refuse runs before anything is
/// written, and the grant is minted after the quota lock is released.
/// </para>
/// </summary>
public sealed class RegisterFileUseCase(
    IStoredFileRegistrationService registrations,
    IFileDirectTransfer transfer,
    ICurrentUser currentUser,
    IOptions<FilesOptions> options,
    IValidator<RegisterFileCommand> validator) : IRegisterFileUseCase
{
    /// <summary>
    /// Long enough for a slow connection to send a large file, short enough that a URL found in a log
    /// afterwards is worthless; a client that misses it registers again.
    /// </summary>
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

        // The validator has refused an unknown category, so this names a declared one.
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

            // Bound into the grant, so the store refuses other bytes; from the aggregate, which guarantees a SHA-256.
            storedFile.Checksum.Value,
            _uploadWindow,
            cancellationToken);

        return new RegisterFileOutcome(storedFile.Id, grant);
    }
}

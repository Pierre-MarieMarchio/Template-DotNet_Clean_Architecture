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
/// Reserves a place for a file and hands back the right to deposit its bytes. The first half of an
/// upload; <c>ConfirmFileUploadUseCase</c> is the second.
/// <para>
/// <b>The row is committed before the grant is minted, and the order is the whole safety
/// argument.</b> A signed URL names a key, so a grant issued before the commit would authorise bytes
/// against a key that no row names if the commit then failed — content nobody can reach and only the
/// orphan sweep would ever remove. In this order the worst case is the opposite and much cheaper: a
/// registration whose grant was never handed out, which is a pending row the abandonment sweep takes
/// away on its own schedule.
/// </para>
/// <para>
/// Everything that can refuse runs before anything is written: the category's rules (through the
/// validator), its upload modes, a store that cannot sign, and the category's quota for this owner,
/// checked and committed by <see cref="IStoredFileRegistrationService"/> — under the owner's quota
/// lock for a strict category. The grant is minted after the lock is released.
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
    /// How long the client has to deposit. Long enough for a slow connection to send five gigabytes,
    /// short enough that a URL found in a log afterwards is worthless — and a client that misses it
    /// registers again rather than being stuck, since the abandoned registration costs it nothing
    /// but a slot it gets back.
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

            // Bound into the grant, so the store refuses any other bytes. From the aggregate rather
            // than the command, because the value object is what guarantees it is a SHA-256.
            storedFile.Checksum.Value,
            _uploadWindow,
            cancellationToken);

        return new RegisterFileOutcome(storedFile.Id, grant);
    }
}

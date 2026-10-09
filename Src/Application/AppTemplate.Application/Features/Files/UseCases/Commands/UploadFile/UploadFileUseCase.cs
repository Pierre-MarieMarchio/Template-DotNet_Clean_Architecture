using System.Security.Cryptography;
using AppTemplate.Application.Core.Common.Concurrency;
using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Application.Core.Common.Validation;
using AppTemplate.Application.Features.Files.Dtos;
using AppTemplate.Application.Features.Files.Errors;
using AppTemplate.Application.Features.Files.Mapping;
using AppTemplate.Application.Features.Files.Options;
using AppTemplate.Application.Features.Files.Ports.FileContentStore;
using AppTemplate.Application.Features.Files.Services;
using AppTemplate.Domain.Features.Files.Entities;
using AppTemplate.Domain.Features.Files.Repositories;
using AppTemplate.Domain.Features.Files.ValueObjects;
using FluentValidation;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AppTemplate.Application.Features.Files.UseCases.Commands.UploadFile;

/// <summary>
/// Uploads a file through the API: registers it, writes its bytes to the store and confirms the
/// deposit, leaving it <see cref="StoredFileState.Deposited"/> for the background inspection, as a
/// direct upload ends.
/// <para>
/// Three steps, each its own commit or write: the registration is committed <c>Pending</c> through
/// <see cref="IStoredFileRegistrationService"/> (under the quota lock for a strict category), the
/// bytes are written outside any lock, and <see cref="IStoredFileDepositService"/> commits the
/// confirmation. When the write or the confirmation fails, the registration is removed at once so
/// its quota slot is free again; if that removal fails too, the abandoned-registration purge and the
/// orphan sweep reclaim the row and the bytes.
/// </para>
/// </summary>
public sealed class UploadFileUseCase(
    IStoredFileRegistrationService registrations,
    IStoredFileDepositService deposits,
    IFileContentStore content,
    IStoredFileRepository repository,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    IDateTimeProvider dateTimeProvider,
    IOptions<FilesOptions> options,
    IValidator<UploadFileCommand> validator,
    ILogger<UploadFileUseCase> logger) : IUploadFileUseCase
{
    public async Task<Result<Versioned<StoredFileDto>>> ExecuteAsync(
        UploadFileCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var userId = currentUser.RequireUserId();

        if (userId.IsFailure)
        {
            return userId.To<Versioned<StoredFileDto>>();
        }

        var validation = await validator.EnsureValidAsync(command, cancellationToken);

        if (validation.IsFailure)
        {
            return validation.To<Versioned<StoredFileDto>>();
        }

        // The validator has refused an unknown category, so this names a declared one.
        var category = FileCategory.Create(command.Category);

        if (!options.Value.Find(category)!.AllowsApiUpload)
        {
            return Result.Failure<Versioned<StoredFileDto>>(StoredFileErrors.ApiUploadNotAllowed(category.Value));
        }

        // Before the registration, which commits the digest, and so before any lock is taken.
        string checksum = await HashAsync(command.Content, cancellationToken);

        var registration = await registrations.RegisterAsync(
            userId.Value,
            category,
            command.Name,
            command.MediaType,
            command.SizeInBytes,
            checksum,
            cancellationToken);

        if (registration.IsFailure)
        {
            return registration.To<Versioned<StoredFileDto>>();
        }

        var storedFile = registration.Value;
        Result confirmation;

        try
        {
            var written = await content.WriteAsync(
                storedFile.ObjectKey.Value,
                command.Content,
                storedFile.DeclaredMediaType.Value,
                storedFile.Size.Bytes,
                storedFile.Checksum.Value,
                cancellationToken);

            // The digest was computed here from the same bytes, so a refusal is this process's fault.
            if (written != ContentWriteOutcome.Written)
            {
                throw new InvalidOperationException(
                    $"The store refused the content of stored file {storedFile.Id} against the digest computed from it.");
            }

            confirmation = await deposits.ConfirmAsync(storedFile, cancellationToken);
        }
        catch (Exception)
        {
            await RemoveRegistrationAsync(storedFile);
            throw;
        }

        if (confirmation.IsFailure)
        {
            await RemoveRegistrationAsync(storedFile);

            return confirmation.To<Versioned<StoredFileDto>>();
        }

        return StoredFileDtoMapping.ToVersioned(storedFile);
    }

    private static async Task<string> HashAsync(Stream content, CancellationToken cancellationToken)
    {
        byte[] digest = await SHA256.HashDataAsync(content, cancellationToken);
        content.Position = 0;

        return Convert.ToHexStringLower(digest);
    }

    /// <summary>
    /// Best effort, and never cancelled: the request may already be gone. The deletion event lets
    /// the consumer reclaim any bytes that were written.
    /// </summary>
    private async Task RemoveRegistrationAsync(StoredFile storedFile)
    {
        try
        {
            storedFile.Delete(dateTimeProvider.UtcNow);
            repository.Remove(storedFile);
            await unitOfWork.SaveChangesAsync(CancellationToken.None);
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Removing the registration of stored file {StoredFileId} after a failed upload failed; the " +
                "abandoned-registration purge and the orphan sweep will reclaim it.",
                storedFile.Id);
        }
    }
}

using System.Security.Cryptography;
using AppTemplate.Application.Core.Common.Concurrency;
using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Application.Core.Common.Validation;
using AppTemplate.Application.Features.Files.Configuration.Options;
using AppTemplate.Application.Features.Files.Model.Dtos;
using AppTemplate.Application.Features.Files.Model.Errors;
using AppTemplate.Application.Features.Files.Model.Mapping;
using AppTemplate.Application.Features.Files.Ports.FileContentStore;
using AppTemplate.Application.Features.Files.Services;
using AppTemplate.Domain.Features.Files.Model.Entities;
using AppTemplate.Domain.Features.Files.Model.ValueObjects;
using FluentValidation;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AppTemplate.Application.Features.Files.UseCases.Commands.UploadFile;

public sealed class UploadFileUseCase(
    IStoredFileRegistrationService registrations,
    IStoredFileDepositService deposits,
    IFileContentStore content,
    ICurrentUser currentUser,
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

        var category = FileCategory.Create(command.Category);

        if (!options.Value.Find(category)!.AllowsApiUpload)
        {
            return Result.Failure<Versioned<StoredFileDto>>(StoredFileErrors.ApiUploadNotAllowed(category.Value));
        }

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

    private async Task RemoveRegistrationAsync(StoredFile storedFile)
    {
        try
        {
            await registrations.WithdrawAsync(storedFile, CancellationToken.None);
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

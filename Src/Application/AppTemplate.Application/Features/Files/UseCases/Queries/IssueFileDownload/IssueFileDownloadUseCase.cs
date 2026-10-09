using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Application.Core.Common.Validation;
using AppTemplate.Application.Features.Files.Errors;
using AppTemplate.Application.Features.Files.Ports.FileContentStore;
using AppTemplate.Application.Features.Files.Ports.FileDirectTransfer;
using AppTemplate.Application.Features.Files.Services;
using AppTemplate.Domain.Features.Files.ValueObjects;
using FluentValidation;

namespace AppTemplate.Application.Features.Files.UseCases.Queries.IssueFileDownload;

/// <summary>
/// Turns "this caller may read this file" into a short-lived signed URL, or — when the store cannot
/// sign — into the opened content.
/// <para>
/// The ownership check happens here and nowhere else: a signed URL is a bearer right with no
/// identity attached. Only <see cref="StoredFileState.Available"/> is served, by name, so a state
/// added later is refused until someone decides otherwise.
/// </para>
/// </summary>
public sealed class IssueFileDownloadUseCase(
    IStoredFileService files,
    IFileDirectTransfer transfer,
    IFileContentStore content,
    IValidator<IssueFileDownloadQuery> validator) : IIssueFileDownloadUseCase
{
    /// <summary>
    /// Minutes: the URL goes into a <c>Location</c> header, so into histories, referrers and proxy
    /// logs, none of which can be revoked.
    /// </summary>
    private static readonly TimeSpan _downloadWindow = TimeSpan.FromMinutes(5);

    public async Task<Result<FileDownload>> ExecuteAsync(
        IssueFileDownloadQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var validation = await validator.EnsureValidAsync(query, cancellationToken);

        if (validation.IsFailure)
        {
            return validation.To<FileDownload>();
        }

        var access = await files.LoadOwnedAsync(query.StoredFileId, precondition: null, cancellationToken);

        if (access.IsFailure)
        {
            return access.To<FileDownload>();
        }

        var storedFile = access.Value;

        if (storedFile.State != StoredFileState.Available)
        {
            // Two answers, because only one of them is worth waiting for.
            return Result.Failure<FileDownload>(
                storedFile.State == StoredFileState.Quarantined
                    ? StoredFileErrors.FileQuarantined(storedFile.Id)
                    : StoredFileErrors.FileNotAvailable(storedFile.Id));
        }

        string objectKey = storedFile.ObjectKey.Value;
        string fileName = storedFile.Name.Value;
        string mediaType = storedFile.DeclaredMediaType.Value;

        if (transfer.IsSupported)
        {
            var grant = await transfer.CreateDownloadGrantAsync(
                objectKey,
                fileName,
                mediaType,
                _downloadWindow,
                cancellationToken);

            return FileDownload.RedirectTo(grant, fileName, mediaType, storedFile.Size.Bytes);
        }

        // An available file with no bytes is a store fault, not something the caller can act on.
        var stream = await content.OpenReadAsync(objectKey, cancellationToken)
            ?? throw new InvalidOperationException(
                $"Stored file '{storedFile.Id}' is available but nothing is stored under its object key.");

        return FileDownload.Streamed(stream, fileName, mediaType, storedFile.Size.Bytes);
    }
}

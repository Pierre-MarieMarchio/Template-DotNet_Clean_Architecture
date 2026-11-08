using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Application.Features.Files.Model.Errors;
using AppTemplate.Application.Features.Files.Ports.FileContentStore;
using AppTemplate.Application.Features.Files.Ports.FileDirectTransfer;
using AppTemplate.Application.Features.Files.Services;
using AppTemplate.Domain.Features.Files.Model.ValueObjects;
using Mediator;
using Microsoft.Extensions.Logging;

namespace AppTemplate.Application.Features.Files.UseCases.Queries.IssueFileDownload;

public sealed record IssueFileDownloadQuery(Guid StoredFileId) : IQuery<Result<FileDownload>>;

public sealed class IssueFileDownloadQueryHandler(
    IStoredFileService files,
    IFileDirectTransfer transfer,
    IFileContentStore content,
    ILogger<IssueFileDownloadQueryHandler> logger) : IQueryHandler<IssueFileDownloadQuery, Result<FileDownload>>
{
    private static readonly TimeSpan _downloadWindow = TimeSpan.FromMinutes(5);

    public async ValueTask<Result<FileDownload>> Handle(
        IssueFileDownloadQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var access = await files.LoadReadableAsync(query.StoredFileId, cancellationToken);

        if (access.IsFailure)
        {
            return access.To<FileDownload>();
        }

        var storedFile = access.Value;

        if (storedFile.State != StoredFileState.Available)
        {
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

        var stream = await content.OpenReadAsync(objectKey, cancellationToken);

        if (stream is null)
        {
            logger.LogWarning(
                "Stored file {StoredFileId} is available but nothing is stored under '{ObjectKey}'.",
                storedFile.Id,
                objectKey);

            return Result.Failure<FileDownload>(StoredFileErrors.FileNotAvailable(storedFile.Id));
        }

        return FileDownload.Streamed(stream, fileName, mediaType, storedFile.Size.Bytes);
    }
}

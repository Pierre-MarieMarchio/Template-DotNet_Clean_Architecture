using AppTemplate.Application.Features.Files.Ports.FileDirectTransfer;

namespace AppTemplate.Infrastructure.Storage.Features.Files.Transfers;

/// <summary>
/// <see cref="IFileDirectTransfer"/> for the local file system, which cannot sign a URL: uploads go
/// through the API and downloads are streamed by it.
/// </summary>
internal sealed class FileSystemFileDirectTransfer : IFileDirectTransfer
{
    public bool IsSupported => false;

    public Task<IssuedUploadGrant> CreateUploadGrantAsync(
        string objectKey,
        string declaredMediaType,
        long sizeInBytes,
        string declaredSha256,
        TimeSpan lifetime,
        CancellationToken cancellationToken = default) =>
        throw Unsupported();

    public Task<IssuedDownloadGrant> CreateDownloadGrantAsync(
        string objectKey,
        string downloadFileName,
        string declaredMediaType,
        TimeSpan lifetime,
        CancellationToken cancellationToken = default) =>
        throw Unsupported();

    private static NotSupportedException Unsupported() =>
        new("The file system store cannot sign URLs; check IFileDirectTransfer.IsSupported first.");
}

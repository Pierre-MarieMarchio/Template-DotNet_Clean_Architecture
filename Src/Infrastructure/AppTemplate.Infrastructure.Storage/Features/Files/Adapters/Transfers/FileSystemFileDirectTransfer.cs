using AppTemplate.Application.Features.Files.Ports.FileDirectTransfer;

namespace AppTemplate.Infrastructure.Storage.Features.Files.Adapters.Transfers;

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

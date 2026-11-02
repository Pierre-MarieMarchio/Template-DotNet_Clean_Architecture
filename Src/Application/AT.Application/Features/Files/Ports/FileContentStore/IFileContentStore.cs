namespace AppTemplate.Application.Features.Files.Ports.FileContentStore;

public interface IFileContentStore
{
    Task<ContentWriteOutcome> WriteAsync(
        string objectKey,
        Stream content,
        string declaredMediaType,
        long sizeInBytes,
        string sha256Hex,
        CancellationToken cancellationToken = default);

    Task<Stream?> OpenReadAsync(string objectKey, CancellationToken cancellationToken = default);

    Task<StoredObjectDescription?> DescribeAsync(string objectKey, CancellationToken cancellationToken = default);

    Task DeleteAsync(string objectKey, CancellationToken cancellationToken = default);
}

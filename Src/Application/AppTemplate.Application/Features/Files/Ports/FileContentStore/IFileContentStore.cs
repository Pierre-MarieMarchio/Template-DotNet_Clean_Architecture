namespace AppTemplate.Application.Features.Files.Ports.FileContentStore;

/// <summary>
/// The bytes of stored files, addressed by object key: write, read, describe, remove. No signature names a domain
/// type (<c>StorageVocabularyTests</c>); signed URLs are a separate capability,
/// <see cref="FileDirectTransfer.IFileDirectTransfer"/>.
/// </summary>
public interface IFileContentStore
{
    /// <summary>
    /// Stores <paramref name="content"/> under <paramref name="objectKey"/> provided it is exactly
    /// <paramref name="sizeInBytes"/> long with the SHA-256 <paramref name="sha256Hex"/>.
    /// <para>
    /// Atomic: on a mismatch, or when the call throws, nothing is stored under the key. A key is bound to one
    /// declared digest, so writing it twice leaves the same bytes.
    /// </para>
    /// </summary>
    Task<ContentWriteOutcome> WriteAsync(
        string objectKey,
        Stream content,
        string declaredMediaType,
        long sizeInBytes,
        string sha256Hex,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Opens the object for reading; the caller disposes the stream. Null when nothing is stored under the key.
    /// </summary>
    Task<Stream?> OpenReadAsync(string objectKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// What is really stored under a key, as the store measured it, never the client's claim; null when nothing is.
    /// </summary>
    Task<StoredObjectDescription?> DescribeAsync(string objectKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes the object. Idempotent, because the deletion consumer and the orphan sweep may both reach the same
    /// key.
    /// </summary>
    Task DeleteAsync(string objectKey, CancellationToken cancellationToken = default);
}

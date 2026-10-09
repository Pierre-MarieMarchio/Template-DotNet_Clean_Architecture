namespace AppTemplate.Application.Features.Files.Ports.FileContentStore;

/// <summary>
/// The bytes of stored files, addressed by object key: write one object, read it, describe it,
/// remove it.
/// <para>
/// No signature names a domain type: a <c>Store</c> has no aggregate behind it
/// (<c>StorageVocabularyTests</c>). Signed URLs are a separate capability,
/// <see cref="FileDirectTransfer.IFileDirectTransfer"/>, which not every store has.
/// </para>
/// </summary>
public interface IFileContentStore
{
    /// <summary>
    /// Stores <paramref name="content"/> under <paramref name="objectKey"/>, provided it is exactly
    /// <paramref name="sizeInBytes"/> long and has the SHA-256 <paramref name="sha256Hex"/>.
    /// <para>
    /// Atomic: on <see cref="ContentWriteOutcome.ContentMismatch"/>, or when the call throws,
    /// nothing is stored under the key. Writing a key twice leaves one object; since a key is bound
    /// to one declared digest, it holds the same bytes either way.
    /// </para>
    /// </summary>
    /// <param name="sha256Hex">The declared digest, 64 hexadecimal characters.</param>
    Task<ContentWriteOutcome> WriteAsync(
        string objectKey,
        Stream content,
        string declaredMediaType,
        long sizeInBytes,
        string sha256Hex,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Opens the object for reading. The caller owns and disposes the stream.
    /// </summary>
    /// <returns><c>null</c> when nothing is stored under the key.</returns>
    Task<Stream?> OpenReadAsync(string objectKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// What is really stored under a key: the store's own measurement, never the client's claim,
    /// which is what makes confirmation worth anything.
    /// </summary>
    /// <returns><c>null</c> when nothing is stored under that key.</returns>
    Task<StoredObjectDescription?> DescribeAsync(string objectKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes the object. Idempotent: deleting a key that holds nothing succeeds, because the
    /// deletion consumer and the orphan sweep may both reach the same key.
    /// </summary>
    Task DeleteAsync(string objectKey, CancellationToken cancellationToken = default);
}

namespace AppTemplate.Application.Features.Files.Ports.FileDirectTransfer;

/// <summary>
/// Signed URLs that let a client write or read one object against the store without the bytes passing through the
/// API; some stores lack it, so check <see cref="IsSupported"/>.
/// <para>
/// A grant is a bearer right: the use case makes the ownership check before asking, and lifetimes are short.
/// </para>
/// </summary>
public interface IFileDirectTransfer
{
    /// <summary>Whether this store can mint grants. When false, both methods throw.</summary>
    bool IsSupported { get; }

    /// <summary>A right to deposit one object for <paramref name="lifetime"/>.</summary>
    /// <param name="declaredMediaType">Bound into the grant, so the deposit cannot claim another.</param>
    /// <param name="sizeInBytes">Bound into the grant, so the store refuses another length.</param>
    /// <param name="declaredSha256">Bound into the grant: the store refuses other bytes, so a grant authorises one
    /// body and cannot be replayed to swap content after inspection.</param>
    /// <exception cref="NotSupportedException">When <see cref="IsSupported"/> is false.</exception>
    Task<IssuedUploadGrant> CreateUploadGrantAsync(
        string objectKey,
        string declaredMediaType,
        long sizeInBytes,
        string declaredSha256,
        TimeSpan lifetime,
        CancellationToken cancellationToken = default);

    /// <summary>A right to read one object for <paramref name="lifetime"/>.</summary>
    /// <param name="downloadFileName">The name offered to save under; it addresses nothing.</param>
    /// <exception cref="NotSupportedException">When <see cref="IsSupported"/> is false.</exception>
    Task<IssuedDownloadGrant> CreateDownloadGrantAsync(
        string objectKey,
        string downloadFileName,
        string declaredMediaType,
        TimeSpan lifetime,
        CancellationToken cancellationToken = default);
}

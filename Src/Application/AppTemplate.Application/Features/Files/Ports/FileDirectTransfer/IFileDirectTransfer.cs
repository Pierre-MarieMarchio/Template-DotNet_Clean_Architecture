namespace AppTemplate.Application.Features.Files.Ports.FileDirectTransfer;

/// <summary>
/// Signed URLs that let a client write or read one object directly against the store, without the
/// bytes passing through the API. A capability some stores lack: check <see cref="IsSupported"/>
/// before asking for a grant.
/// <para>
/// A grant is a bearer right. Whoever holds the URL uses it, so the ownership check is the use
/// case's, made before asking, and every lifetime passed here is short.
/// </para>
/// </summary>
public interface IFileDirectTransfer
{
    /// <summary>Whether this store can mint grants. When <c>false</c>, both methods throw.</summary>
    bool IsSupported { get; }

    /// <summary>A right to deposit the bytes of one object, valid for <paramref name="lifetime"/>.</summary>
    /// <param name="declaredMediaType">Bound into the grant, so the deposit cannot claim another.</param>
    /// <param name="sizeInBytes">Bound into the grant, so the store refuses a deposit of another length.</param>
    /// <param name="declaredSha256">
    /// 64 hexadecimal characters, bound into the grant: the store refuses other bytes, so a grant
    /// authorises one body and cannot be replayed to swap content after inspection.
    /// </param>
    /// <exception cref="NotSupportedException">When <see cref="IsSupported"/> is <c>false</c>.</exception>
    Task<IssuedUploadGrant> CreateUploadGrantAsync(
        string objectKey,
        string declaredMediaType,
        long sizeInBytes,
        string declaredSha256,
        TimeSpan lifetime,
        CancellationToken cancellationToken = default);

    /// <summary>A right to read one object, valid for <paramref name="lifetime"/>.</summary>
    /// <param name="downloadFileName">The name offered to save under; it addresses nothing.</param>
    /// <exception cref="NotSupportedException">When <see cref="IsSupported"/> is <c>false</c>.</exception>
    Task<IssuedDownloadGrant> CreateDownloadGrantAsync(
        string objectKey,
        string downloadFileName,
        string declaredMediaType,
        TimeSpan lifetime,
        CancellationToken cancellationToken = default);
}

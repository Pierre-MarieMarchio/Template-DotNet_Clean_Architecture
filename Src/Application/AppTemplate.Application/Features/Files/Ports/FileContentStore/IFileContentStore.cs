namespace AppTemplate.Application.Features.Files.Ports.FileContentStore;

/// <summary>
/// The object store, as this application uses it: mint a right to write one object, mint a right to
/// read one object, say what is actually under a key, and remove it.
/// <para>
/// <b>It never carries a byte, and that is the whole point.</b> Inbound bodies are capped at 64 KiB
/// by <c>RequestLimitsOptions</c> and the idempotency filter buffers and SHA-256s every <c>POST</c>
/// body before a handler sees it, so routing a real upload through this process is not viable. The
/// client deposits directly against a signed URL this port produces, and comes back to confirm.
/// The same reasoning applies to reading: the API answers with a right, not with content.
/// </para>
/// <para>
/// <b>A grant is a bearer right.</b> Whoever holds the URL can use it, so nothing about it is a
/// substitute for the ownership check a use case makes before asking for one, and every lifetime
/// passed here is short. The decision about who may read a file belongs to the domain; this port
/// only knows how to express a decision already made.
/// </para>
/// <para>
/// <b>No signature here names a type from <c>AppTemplate.Domain.Features</c>, deliberately.</b>
/// The word <c>Store</c> promises storage with no aggregate behind it — see
/// <c>CONTRIBUTING.md</c>'s four storage words — so this speaks in <see cref="string"/>,
/// <see cref="long"/> and the records beside it. An adapter for it needs no reference to the domain
/// at all, and <c>StorageVocabularyTests</c> is what holds that.
/// </para>
/// <para>
/// Four operations is the ceiling <c>PortConventionTests</c> imposes, and this port is at it. A
/// fifth capability — listing what the store holds — is <see cref="FileContentInventory.IFileContentInventory"/>
/// precisely because there was no room to pretend it was part of this one.
/// </para>
/// </summary>
public interface IFileContentStore
{
    /// <summary>
    /// A right to deposit the bytes of one object, valid for <paramref name="lifetime"/>.
    /// </summary>
    /// <param name="declaredMediaType">Bound into the grant so the deposit cannot claim a different
    /// one. It is still only a claim about the bytes — nothing here reads them.</param>
    /// <param name="sizeInBytes">Bound into the grant as well, so a client that declared one size
    /// and deposits another is refused by the store rather than at confirmation.</param>
    Task<IssuedUploadGrant> CreateUploadGrantAsync(
        string objectKey,
        string declaredMediaType,
        long sizeInBytes,
        TimeSpan lifetime,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// A right to read one object, valid for <paramref name="lifetime"/>.
    /// </summary>
    /// <param name="downloadFileName">Offered to the client as the name to save under. It is a
    /// label the user chose and it addresses nothing: <paramref name="objectKey"/> is what names the
    /// object.</param>
    Task<IssuedDownloadGrant> CreateDownloadGrantAsync(
        string objectKey,
        string downloadFileName,
        string declaredMediaType,
        TimeSpan lifetime,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// What is really stored under a key. This is the only fact in the whole feature that does not
    /// come from the client, which is why confirmation is worth anything at all.
    /// </summary>
    /// <returns><c>null</c> when nothing is stored under that key — a deposit that never happened,
    /// or one already reclaimed.</returns>
    Task<StoredObjectDescription?> DescribeAsync(string objectKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes the object. Deleting one that is not there must succeed silently: both the fast path
    /// that reacts to a deletion and the sweep that reclaims unreferenced bytes may reach the same
    /// key, and neither is coordinated with the other.
    /// </summary>
    Task DeleteAsync(string objectKey, CancellationToken cancellationToken = default);
}

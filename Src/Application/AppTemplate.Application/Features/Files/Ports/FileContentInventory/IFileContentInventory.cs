using AppTemplate.Application.Common.Results;

namespace AppTemplate.Application.Features.Files.Ports.FileContentInventory;

/// <summary>
/// What the object store holds, enumerated. A separate port from
/// <see cref="FileContentStore.IFileContentStore"/> because it is a separate capability:
/// reconciliation, rather than one object's life.
/// <para>
/// This is what the orphan sweep reads. Reclaiming storage cannot depend on a message being
/// delivered — see <c>CONTRIBUTING.md</c>'s "Correctness does not depend on event delivery" — so it
/// is done by difference: enumerate what is stored, ask which of those keys a live row still names,
/// and delete the rest. Nothing survives a deleted row to say what is owed, because nothing has to.
/// </para>
/// </summary>
public interface IFileContentInventory
{
    /// <summary>
    /// One page of the keys stored under <paramref name="prefix"/>.
    /// </summary>
    /// <param name="prefix">Limits the walk to part of the key namespace. Keys are minted as
    /// <c>&lt;partition&gt;/&lt;time segment&gt;/&lt;name&gt;</c>, so a prefix is how a caller pays
    /// for one slice of the store rather than all of it.</param>
    /// <param name="continuationToken">
    /// <c>null</c> for the first page; otherwise the previous page's <see cref="PagedResult{TItem}.NextCursor"/>.
    /// Opaque, and the store's own — this application never parses one.
    /// </param>
    /// <returns>
    /// The keys, and a continuation token when more remain. Only the keyset half of
    /// <see cref="PagedResult{TItem}"/> is populated: the offset half — page number, total count —
    /// is <c>null</c>, because no store will count its own contents to answer a listing.
    /// </returns>
    Task<PagedResult<string>> ListKeysAsync(
        string prefix,
        string? continuationToken,
        int pageSize,
        CancellationToken cancellationToken = default);
}

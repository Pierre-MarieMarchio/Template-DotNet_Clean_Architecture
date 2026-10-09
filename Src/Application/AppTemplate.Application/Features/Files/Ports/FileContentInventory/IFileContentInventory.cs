using AppTemplate.Application.Core.Common.Results;

namespace AppTemplate.Application.Features.Files.Ports.FileContentInventory;

/// <summary>
/// What the store holds, enumerated — the input of the orphan sweep, which reclaims by difference
/// (see <c>docs/DECISIONS.md</c>, "Correctness does not depend on event delivery").
/// </summary>
public interface IFileContentInventory
{
    /// <summary>
    /// One page of the keys stored under <paramref name="prefix"/>, in ordinal (byte) order.
    /// </summary>
    /// <param name="prefix">Limits the walk to part of the key namespace.</param>
    /// <param name="startAfter">
    /// <c>null</c> to start at the beginning; otherwise only keys ordinally greater than it are
    /// listed. A walk resumes from the last key it saw, across passes and restarts alike.
    /// </param>
    /// <returns>
    /// The keyset half of <see cref="PagedResult{TItem}"/>: the keys, and as
    /// <see cref="PagedResult{TItem}.NextCursor"/> the last of them when more remain, <c>null</c> at
    /// the end. No total: no store counts its contents to answer a listing.
    /// </returns>
    Task<PagedResult<string>> ListKeysAsync(
        string prefix,
        string? startAfter,
        int pageSize,
        CancellationToken cancellationToken = default);
}

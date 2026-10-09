using AppTemplate.Application.Core.Common.Results;

namespace AppTemplate.Application.Features.Files.Ports.FileContentInventory;

/// <summary>
/// What the store holds, enumerated: the orphan sweep's input, which reclaims by difference (see
/// <c>docs/DECISIONS.md</c>).
/// </summary>
public interface IFileContentInventory
{
    /// <summary>One page of the keys under <paramref name="prefix"/>, in ordinal (byte) order.</summary>
    /// <param name="startAfter">Null to start at the beginning; otherwise only keys ordinally greater are listed, so
    /// a walk resumes across passes and restarts.</param>
    /// <returns>The keys, with the last of them as <see cref="PagedResult{TItem}.NextCursor"/> when more remain and
    /// no total.</returns>
    Task<PagedResult<string>> ListKeysAsync(
        string prefix,
        string? startAfter,
        int pageSize,
        CancellationToken cancellationToken = default);
}

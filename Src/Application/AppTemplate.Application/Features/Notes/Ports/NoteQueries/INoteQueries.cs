using AppTemplate.Application.Core.Common.Collections;
using AppTemplate.Application.Core.Common.Concurrency;
using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Application.Features.Notes.Dtos;
using AppTemplate.Domain.Core.Common.Primitives;

namespace AppTemplate.Application.Features.Notes.Ports.NoteQueries;

/// <summary>
/// The read half of a note's storage: projections onto DTOs, with nothing staged and nothing
/// tracked. Every method names the owner and scopes to it, which is what makes somebody else's note
/// and a missing one the same empty answer.
/// </summary>
public interface INoteQueries
{
    /// <summary>One page of the owner's notes. <paramref name="request"/> has already cleared the whitelist.</summary>
    Task<PagedResult<NoteSummaryDto>> GetForOwnerAsync(
        UserId ownerId,
        FeaturePageRequest<NoteFilter> request,
        CancellationToken cancellationToken = default);

    /// <returns>
    /// The note and the version a caller has to name to change it, read by one statement so the two
    /// agree; or <c>null</c> when the note does not exist or is not <paramref name="ownerId"/>'s.
    /// </returns>
    Task<Versioned<NoteDto>?> GetAsync(
        UserId ownerId,
        Guid noteId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Every owner id that still holds a note, regardless of that account's fate: the
    /// deleted-account sweep asks, then decides the fate itself through the account directory.
    /// Distinct, unordered.
    /// </summary>
    Task<IReadOnlyList<UserId>> ListDataOwnerIdsAsync(CancellationToken cancellationToken = default);
}

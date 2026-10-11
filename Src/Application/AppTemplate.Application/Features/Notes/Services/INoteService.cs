using AppTemplate.Application.Core.Common.Concurrency;
using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Application.Features.Notes.Dtos;
using AppTemplate.Application.Features.Notes.Errors;

namespace AppTemplate.Application.Features.Notes.Services;

/// <summary>Everything a caller can do with their own notes, one method per operation. Each answers
/// on behalf of the authenticated caller, which it reads itself: no method takes an owner, so no
/// caller can name somebody else's.</summary>
/// <remarks>A note without rules to protect is a feature without a domain model, so this is the
/// whole application layer of it. Reads stage and commit nothing — the only separation CQS asks
/// for. Every failure for a note the caller cannot see is <see cref="NoteErrors.NoteNotFound"/>,
/// whether it is missing or somebody else's.</remarks>
public interface INoteService
{
    /// <summary>One page of the caller's notes, sorted and paged.</summary>
    Task<Result<PagedResult<NoteSummaryDto>>> ListAsync(
        NoteListQueryDto query,
        CancellationToken cancellationToken = default);

    /// <summary>One note, and the version a caller has to name to change it.</summary>
    Task<Result<Versioned<NoteDto>>> GetAsync(
        Guid noteId,
        CancellationToken cancellationToken = default);

    /// <summary>Creates a note owned by the caller.</summary>
    Task<Result<Versioned<NoteDto>>> CreateAsync(
        NoteContentDto content,
        CancellationToken cancellationToken = default);

    /// <summary>Replaces a note's text.</summary>
    /// <param name="precondition">The versions the caller will accept, or <c>null</c> for an unconditional update.</param>
    /// <returns>The updated note and its new version; <see cref="NoteErrors.NoteNotFound"/>;
    /// <see cref="ConcurrencyErrors.PreconditionFailed"/> when the note is no longer at a version the
    /// caller named; or a validation failure.</returns>
    Task<Result<Versioned<NoteDto>>> UpdateAsync(
        Guid noteId,
        NoteContentDto content,
        VersionPrecondition? precondition,
        CancellationToken cancellationToken = default);

    /// <summary>Deletes a note, on the same terms as <see cref="UpdateAsync"/>.</summary>
    Task<Result> DeleteAsync(
        Guid noteId,
        VersionPrecondition? precondition,
        CancellationToken cancellationToken = default);
}

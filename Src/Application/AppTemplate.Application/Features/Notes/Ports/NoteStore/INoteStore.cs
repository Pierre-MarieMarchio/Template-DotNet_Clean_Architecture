using AppTemplate.Application.Core.Common.Concurrency;
using AppTemplate.Application.Features.Notes.Dtos;
using AppTemplate.Domain.Core.Common.Primitives;

namespace AppTemplate.Application.Features.Notes.Ports.NoteStore;

/// <summary>
/// The write half of a note's storage. There is no aggregate behind it: a note is data with an
/// owner, so a write takes the text itself. Every method names the owner and scopes to it.
/// <para>
/// A write only stages. <see cref="Core.Common.Ports.IUnitOfWork"/> commits, so the caller decides
/// when the transaction ends. The read half is <see cref="NoteQueries.INoteQueries"/>.
/// </para>
/// </summary>
public interface INoteStore
{
    /// <summary>Stages a new note for <paramref name="ownerId"/>.</summary>
    void Add(UserId ownerId, Guid noteId, NoteContentDto content, DateTimeOffset createdAt);

    /// <summary>Stages the replacement of a note's text.</summary>
    /// <param name="precondition">The versions the caller will accept, or <c>null</c> for any.</param>
    /// <returns>
    /// <see cref="NoteChangeStatus.Staged"/>, in which case the write is bound to the version it
    /// loaded and the commit refuses it if that version has moved; or why nothing was staged.
    /// </returns>
    Task<NoteChangeStatus> StageReplacementAsync(
        UserId ownerId,
        Guid noteId,
        NoteContentDto content,
        DateTimeOffset modifiedAt,
        VersionPrecondition? precondition,
        CancellationToken cancellationToken = default);

    /// <summary>Stages the removal of a note, on the terms of <see cref="StageReplacementAsync"/>.</summary>
    Task<NoteChangeStatus> StageRemovalAsync(
        UserId ownerId,
        Guid noteId,
        VersionPrecondition? precondition,
        CancellationToken cancellationToken = default);
}

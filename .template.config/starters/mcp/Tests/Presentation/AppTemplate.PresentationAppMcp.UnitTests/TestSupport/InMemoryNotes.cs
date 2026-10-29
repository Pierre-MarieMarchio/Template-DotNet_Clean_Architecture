using System.Collections.Concurrent;
using AppTemplate.Application.Core.Common.Concurrency;
using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Application.Features.Notes.Model.Dtos;
using AppTemplate.Application.Features.Notes.Model.Errors;
using AppTemplate.Application.Features.Notes.Services;

namespace AppTemplate.PresentationApp.UnitTests.TestSupport;

internal sealed class InMemoryNotes(ICurrentUser caller) : INoteService
{
    private static readonly ConcurrentDictionary<Guid, (Guid Owner, NoteDto Note)> _notes = new();

    public Task<Result<PagedResult<NoteSummaryDto>>> ListAsync(NoteListQueryDto query, CancellationToken cancellationToken = default)
    {
        NoteSummaryDto[] owned = [.. _notes.Values
            .Where(entry => entry.Owner == caller.UserId?.Value)
            .Select(entry => new NoteSummaryDto(entry.Note.Id, entry.Note.Title, entry.Note.CreatedAt, entry.Note.LastModifiedAt))];

        return Task.FromResult(Result.Success(PagedResult.Offset<NoteSummaryDto>(owned, 1, 20, owned.Length)));
    }

    public Task<Result<Versioned<NoteDto>>> GetAsync(Guid noteId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_notes.TryGetValue(noteId, out var entry) && entry.Owner == caller.UserId?.Value
            ? Result.Success(new Versioned<NoteDto>(entry.Note, 1))
            : Result.Failure<Versioned<NoteDto>>(NoteErrors.NoteNotFound(noteId)));

    public Task<Result<Versioned<NoteDto>>> CreateAsync(NoteContentDto content, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);

        var owner = caller.UserId ?? throw new InvalidOperationException("A note is created by a caller.");
        var note = new NoteDto(Guid.CreateVersion7(), content.Title, content.Body, DateTimeOffset.UtcNow, null);
        _notes[note.Id] = (owner.Value, note);

        return Task.FromResult(Result.Success(new Versioned<NoteDto>(note, 1)));
    }

    public Task<Result<Versioned<NoteDto>>> UpdateAsync(
        Guid noteId,
        NoteContentDto content,
        VersionPrecondition? precondition,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("No tool updates a note.");

    public Task<Result> DeleteAsync(Guid noteId, VersionPrecondition? precondition, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("No tool deletes a note.");
}

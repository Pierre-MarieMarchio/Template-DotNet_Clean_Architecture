using AppTemplate.Application.Core.Common.Concurrency;
using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Application.Features.Notes.Model.Dtos;

namespace AppTemplate.Application.Features.Notes.Services;

public interface INoteService
{
    Task<Result<PagedResult<NoteSummaryDto>>> ListAsync(
        NoteListQueryDto query,
        CancellationToken cancellationToken = default);

    Task<Result<Versioned<NoteDto>>> GetAsync(
        Guid noteId,
        CancellationToken cancellationToken = default);

    Task<Result<Versioned<NoteDto>>> CreateAsync(
        NoteContentDto content,
        CancellationToken cancellationToken = default);

    Task<Result<Versioned<NoteDto>>> UpdateAsync(
        Guid noteId,
        NoteContentDto content,
        VersionPrecondition? precondition,
        CancellationToken cancellationToken = default);

    Task<Result> DeleteAsync(
        Guid noteId,
        VersionPrecondition? precondition,
        CancellationToken cancellationToken = default);
}

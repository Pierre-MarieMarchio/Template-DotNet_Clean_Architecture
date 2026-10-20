using AppTemplate.Application.Core.Common.Collections;
using AppTemplate.Application.Core.Common.Concurrency;
using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Application.Features.Notes.Model.Dtos;
using AppTemplate.Domain.Core.Common.Primitives;

namespace AppTemplate.Application.Features.Notes.Ports.NoteQueries;

public interface INoteQueries
{
    Task<PagedResult<NoteSummaryDto>> GetForOwnerAsync(
        UserId ownerId,
        FeaturePageRequest<NoteFilter> request,
        CancellationToken cancellationToken = default);

    Task<Versioned<NoteDto>?> GetAsync(
        UserId ownerId,
        Guid noteId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<UserId>> ListDataOwnerIdsAsync(CancellationToken cancellationToken = default);
}

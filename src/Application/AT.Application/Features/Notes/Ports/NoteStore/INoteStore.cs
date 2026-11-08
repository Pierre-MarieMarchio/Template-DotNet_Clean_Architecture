using AppTemplate.Application.Core.Common.Concurrency;
using AppTemplate.Application.Features.Notes.Model.Dtos;
using AppTemplate.Domain.Core.Common.Primitives;

namespace AppTemplate.Application.Features.Notes.Ports.NoteStore;

public interface INoteStore
{
    void Add(UserId ownerId, Guid noteId, NoteContentDto content, DateTimeOffset createdAt);

    Task<NoteChangeStatus> StageReplacementAsync(
        UserId ownerId,
        Guid noteId,
        NoteContentDto content,
        DateTimeOffset modifiedAt,
        VersionPrecondition? precondition,
        CancellationToken cancellationToken = default);

    Task<NoteChangeStatus> StageRemovalAsync(
        UserId ownerId,
        Guid noteId,
        VersionPrecondition? precondition,
        CancellationToken cancellationToken = default);

    Task<int> StageRemovalForOwnerAsync(
        UserId ownerId,
        CancellationToken cancellationToken = default);
}

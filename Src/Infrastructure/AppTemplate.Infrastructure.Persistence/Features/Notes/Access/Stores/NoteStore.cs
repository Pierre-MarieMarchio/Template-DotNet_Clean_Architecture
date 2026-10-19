using AppTemplate.Application.Core.Common.Concurrency;
using AppTemplate.Application.Features.Notes.Model.Dtos;
using AppTemplate.Application.Features.Notes.Ports.NoteStore;
using AppTemplate.Domain.Core.Common.Primitives;
using AppTemplate.Infrastructure.Persistence.Common.Contexts;
using AppTemplate.Infrastructure.Persistence.Features.Notes.Rows.Models;
using Microsoft.EntityFrameworkCore;

namespace AppTemplate.Infrastructure.Persistence.Features.Notes.Access.Stores;

/// <summary>
/// The adapter for <see cref="INoteStore"/>: it stages changes to <see cref="NoteRecord"/> rows
/// directly, with no mapper, tracker or event source, and never commits.
/// <para>
/// <b>Where a stale write is refused.</b> A change loads the owner's row tracked, checks the
/// caller's versions against it, and stages the edit. EF then writes
/// <c>UPDATE … WHERE "Id" = @id AND xmin = @loaded</c> (or the <c>DELETE</c> likewise), and a row the
/// database no longer finds at that <c>xmin</c> is a rejected write, which the unit of work reports as
/// a conflict. When the caller sent <c>If-Match</c>, the version in that <c>WHERE</c> is one it
/// named, so the database, not the earlier read, has the last word.
/// </para>
/// </summary>
internal sealed class NoteStore(AppDbContext context) : INoteStore
{
    public void Add(UserId ownerId, Guid noteId, NoteContentDto content, DateTimeOffset createdAt)
    {
        ArgumentNullException.ThrowIfNull(ownerId);
        ArgumentNullException.ThrowIfNull(content);

        context.Notes.Add(new NoteRecord
        {
            Id = noteId,
            OwnerId = ownerId.Value,
            Title = content.Title,
            Body = content.Body,
            CreatedAt = createdAt,
        });
    }

    public async Task<NoteChangeStatus> StageReplacementAsync(
        UserId ownerId,
        Guid noteId,
        NoteContentDto content,
        DateTimeOffset modifiedAt,
        VersionPrecondition? precondition,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(ownerId);
        ArgumentNullException.ThrowIfNull(content);

        var (status, record) = await LoadForChangeAsync(ownerId, noteId, precondition, cancellationToken);

        if (record is not null)
        {
            record.Title = content.Title;
            record.Body = content.Body;
            record.LastModifiedAt = modifiedAt;
        }

        return status;
    }

    public async Task<NoteChangeStatus> StageRemovalAsync(
        UserId ownerId,
        Guid noteId,
        VersionPrecondition? precondition,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(ownerId);

        var (status, record) = await LoadForChangeAsync(ownerId, noteId, precondition, cancellationToken);

        if (record is not null)
        {
            context.Notes.Remove(record);
        }

        return status;
    }

    /// <summary>
    /// Stages the removal of the owner's every note in one load, on the same terms as
    /// <see cref="StageRemovalAsync"/>: the rows are loaded tracked so the commit deletes them, and
    /// no version is checked because the caller of this holds none — see the port.
    /// </summary>
    public async Task<int> StageRemovalForOwnerAsync(
        UserId ownerId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(ownerId);

        var records = await context.Notes
            .Where(note => note.OwnerId == ownerId.Value)
            .ToListAsync(cancellationToken);

        context.Notes.RemoveRange(records);

        return records.Count;
    }

    /// <summary>
    /// Ownership first, then the caller's versions, in that order: a note that is not the caller's is
    /// never reported as stale. The row stays tracked at the version it was loaded at, which is what
    /// the commit's <c>WHERE xmin = …</c> compares.
    /// </summary>
    private async Task<(NoteChangeStatus Status, NoteRecord? Record)> LoadForChangeAsync(
        UserId ownerId,
        Guid noteId,
        VersionPrecondition? precondition,
        CancellationToken cancellationToken)
    {
        var record = await context.Notes
            .SingleOrDefaultAsync(note => note.Id == noteId && note.OwnerId == ownerId.Value, cancellationToken);

        if (record is null)
        {
            return (NoteChangeStatus.NotFound, null);
        }

        return precondition is not null && !precondition.IsSatisfiedBy(record.Version)
            ? (NoteChangeStatus.VersionMismatch, null)
            : (NoteChangeStatus.Staged, record);
    }
}

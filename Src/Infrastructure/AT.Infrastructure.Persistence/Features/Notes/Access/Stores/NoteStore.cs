using AppTemplate.Application.Core.Common.Concurrency;
using AppTemplate.Application.Features.Notes.Model.Dtos;
using AppTemplate.Application.Features.Notes.Ports.NoteStore;
using AppTemplate.Domain.Core.Common.Primitives;
using AppTemplate.Infrastructure.Persistence.Common.Contexts;
using AppTemplate.Infrastructure.Persistence.Features.Notes.Rows.Models;
using Microsoft.EntityFrameworkCore;

namespace AppTemplate.Infrastructure.Persistence.Features.Notes.Access.Stores;

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

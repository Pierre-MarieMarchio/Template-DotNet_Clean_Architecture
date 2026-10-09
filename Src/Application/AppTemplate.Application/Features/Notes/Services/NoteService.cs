using AppTemplate.Application.Core.Common.Collections;
using AppTemplate.Application.Core.Common.Concurrency;
using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Application.Core.Common.Validation;
using AppTemplate.Application.Features.Notes.Dtos;
using AppTemplate.Application.Features.Notes.Errors;
using AppTemplate.Application.Features.Notes.Policies;
using AppTemplate.Application.Features.Notes.Ports.NoteQueries;
using AppTemplate.Application.Features.Notes.Ports.NoteStore;
using AppTemplate.Domain.Core.Common.Primitives;
using FluentValidation;

namespace AppTemplate.Application.Features.Notes.Services;

internal sealed class NoteService(
    INoteQueries queries,
    INoteStore store,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    IDateTimeProvider dateTimeProvider,
    IValidator<NoteContentDto> validator) : INoteService
{
    public async Task<Result<PagedResult<NoteSummaryDto>>> ListAsync(
        NoteListQueryDto query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var userId = currentUser.RequireUserId();

        if (userId.IsFailure)
        {
            return userId.To<PagedResult<NoteSummaryDto>>();
        }

        var bound = CollectionBinding.Bind(
            query,
            NoteCollectionPolicy.Instance,
            () => Result.Success(NoteFilter.None),
            NoteCollectionPolicy.CreatedAtField);

        if (bound.IsFailure)
        {
            return bound.To<PagedResult<NoteSummaryDto>>();
        }

        return await queries.GetForOwnerAsync(userId.Value, bound.Value, cancellationToken);
    }

    public async Task<Result<Versioned<NoteDto>>> GetAsync(Guid noteId, CancellationToken cancellationToken = default)
    {
        var userId = currentUser.RequireUserId();

        if (userId.IsFailure)
        {
            return userId.To<Versioned<NoteDto>>();
        }

        return await ReadAsync(userId.Value, noteId, cancellationToken);
    }

    public async Task<Result<Versioned<NoteDto>>> CreateAsync(
        NoteContentDto content,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);

        var userId = currentUser.RequireUserId();

        if (userId.IsFailure)
        {
            return userId.To<Versioned<NoteDto>>();
        }

        var validation = await validator.EnsureValidAsync(content, cancellationToken);

        if (validation.IsFailure)
        {
            return validation.To<Versioned<NoteDto>>();
        }

        var noteId = Guid.CreateVersion7();

        store.Add(userId.Value, noteId, Trimmed(content), dateTimeProvider.UtcNow);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return await ReadAsync(userId.Value, noteId, cancellationToken);
    }

    public async Task<Result<Versioned<NoteDto>>> UpdateAsync(
        Guid noteId,
        NoteContentDto content,
        VersionPrecondition? precondition,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);

        var userId = currentUser.RequireUserId();

        if (userId.IsFailure)
        {
            return userId.To<Versioned<NoteDto>>();
        }

        var validation = await validator.EnsureValidAsync(content, cancellationToken);

        if (validation.IsFailure)
        {
            return validation.To<Versioned<NoteDto>>();
        }

        var status = await store.StageReplacementAsync(
            userId.Value,
            noteId,
            Trimmed(content),
            dateTimeProvider.UtcNow,
            precondition,
            cancellationToken);

        if (status != NoteChangeStatus.Staged)
        {
            return Result.Failure<Versioned<NoteDto>>(FailureOf(status, noteId));
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return await ReadAsync(userId.Value, noteId, cancellationToken);
    }

    public async Task<Result> DeleteAsync(
        Guid noteId,
        VersionPrecondition? precondition,
        CancellationToken cancellationToken = default)
    {
        var userId = currentUser.RequireUserId();

        if (userId.IsFailure)
        {
            return userId;
        }

        var status = await store.StageRemovalAsync(userId.Value, noteId, precondition, cancellationToken);

        if (status != NoteChangeStatus.Staged)
        {
            return Result.Failure(FailureOf(status, noteId));
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }

    // The version is assigned by the database, so a write's answer is read back rather than assembled.
    private async Task<Result<Versioned<NoteDto>>> ReadAsync(
        UserId ownerId,
        Guid noteId,
        CancellationToken cancellationToken)
    {
        var note = await queries.GetAsync(ownerId, noteId, cancellationToken);

        return note is null
            ? Result.Failure<Versioned<NoteDto>>(NoteErrors.NoteNotFound(noteId))
            : note;
    }

    private static NoteContentDto Trimmed(NoteContentDto content) => content with { Title = content.Title.Trim() };

    private static Error FailureOf(NoteChangeStatus status, Guid noteId) => status switch
    {
        NoteChangeStatus.NotFound => NoteErrors.NoteNotFound(noteId),
        NoteChangeStatus.VersionMismatch => ConcurrencyErrors.PreconditionFailed,
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "A staged change has no failure."),
    };
}

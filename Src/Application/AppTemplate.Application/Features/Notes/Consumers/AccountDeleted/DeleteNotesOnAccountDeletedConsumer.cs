using AppTemplate.Application.Core.Common.Events;
using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Application.Features.Notes.Ports.NoteQueries;
using AppTemplate.Application.Features.Notes.Ports.NoteStore;
using AppTemplate.Domain.Core.Common.Primitives;

namespace AppTemplate.Application.Features.Notes.Consumers.AccountDeleted;

/// <summary>The notes' declared fate for the data of a deleted account: <b>deleted</b> — the store
/// removes the owner's rows in one stage and this commits them, as every note write does. Runs
/// after the account's transaction has committed and owns its own commit through
/// <see cref="IUnitOfWork"/>. <b>A fast path, not the guarantee</b>: the
/// <see cref="IDeletedAccountDataPolicy"/> face the sweep calls covers the event never reaching
/// this consumer, and the precondition is re-derived through <see cref="IUserAccountDirectory"/>.</summary>
internal sealed class DeleteNotesOnAccountDeletedConsumer(
    INoteStore notes,
    INoteQueries queries,
    IUserAccountDirectory accounts,
    IUnitOfWork unitOfWork) : IDomainEventConsumer<UserAccountDeleted>, IDeletedAccountDataPolicy
{
    public Task ConsumeAsync(UserAccountDeleted domainEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);

        return ApplyAsync(domainEvent.UserId, cancellationToken);
    }

    public async Task<bool> ApplyAsync(UserId owner, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // The precondition, re-derived: an account that exists again — or never left — keeps its
        // notes, whatever this call was told.
        if (await accounts.ExistsAsync(owner, cancellationToken))
        {
            return false;
        }

        int staged = await notes.StageRemovalForOwnerAsync(owner, cancellationToken);

        // Also what a redelivery finds: the store refuses nothing here, so an empty owner is simply
        // a finished one.
        if (staged == 0)
        {
            return false;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return true;
    }

    public Task<IReadOnlyList<UserId>> ListDataOwnerIdsAsync(
        CancellationToken cancellationToken = default) =>
        queries.ListDataOwnerIdsAsync(cancellationToken);
}

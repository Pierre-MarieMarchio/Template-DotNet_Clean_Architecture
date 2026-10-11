using AppTemplate.Application.Core.Common.Events;
using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Application.Features.Reminders.Ports.ReminderQueries;
using AppTemplate.Domain.Core.Common.Primitives;
using AppTemplate.Domain.Features.Reminders.Repositories;

namespace AppTemplate.Application.Features.Reminders.Consumers.AccountDeleted;

/// <summary>
/// The reminders' declared fate for the data of a deleted account: <b>deleted</b>, whatever the
/// state each reminder is in — a reminder about an item nobody owns is noise waiting to fire.
/// <para>
/// This runs after the transaction that removed the account has already committed, so it owns
/// committing its own change through <see cref="IUnitOfWork"/>. <b>A fast path, not the guarantee</b>:
/// the same <see cref="IDeletedAccountDataPolicy"/> face the sweep calls covers the event never
/// reaching this consumer, and the precondition is re-derived through
/// <see cref="IUserAccountDirectory"/> rather than trusted.
/// </para>
/// </summary>
internal sealed class DeleteRemindersOnAccountDeletedConsumer(
    IReminderRepository reminders,
    IReminderQueries queries,
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
        // reminders, whatever this call was told.
        if (await accounts.ExistsAsync(owner, cancellationToken))
        {
            return false;
        }

        var owned = await reminders.GetForOwnerAsync(owner, cancellationToken);

        // Also what a redelivery finds.
        if (owned.Count == 0)
        {
            return false;
        }

        foreach (var reminder in owned)
        {
            reminders.Remove(reminder);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return true;
    }

    public Task<IReadOnlyList<UserId>> ListDataOwnerIdsAsync(
        CancellationToken cancellationToken = default) =>
        queries.ListDataOwnerIdsAsync(cancellationToken);
}

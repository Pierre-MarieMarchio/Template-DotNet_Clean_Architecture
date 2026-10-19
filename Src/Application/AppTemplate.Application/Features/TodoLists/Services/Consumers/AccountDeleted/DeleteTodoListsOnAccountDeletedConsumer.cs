using AppTemplate.Application.Core.Common.Events;
using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Application.Features.TodoLists.Ports.TodoListQueries;
using AppTemplate.Domain.Core.Common.Primitives;
using AppTemplate.Domain.Features.TodoLists.Repositories;

namespace AppTemplate.Application.Features.TodoLists.Services.Consumers.AccountDeleted;

/// <summary>The to-do lists' declared fate for the data of a deleted account: <b>deleted</b> — the
/// lists, their items and their tags are this feature's rows, so the decision is made here. Runs
/// after the account's transaction has committed and owns its own commit through
/// <see cref="IUnitOfWork"/>. <b>A fast path, not the guarantee</b>: the
/// <see cref="IDeletedAccountDataPolicy"/> face the sweep calls covers the event never reaching this
/// consumer, and the precondition is re-derived through <see cref="IUserAccountDirectory"/>, so a
/// redelivery cannot delete a live owner's lists.</summary>
internal sealed class DeleteTodoListsOnAccountDeletedConsumer(
    ITodoListRepository lists,
    ITodoListQueries queries,
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
        // lists, whatever this call was told.
        if (await accounts.ExistsAsync(owner, cancellationToken))
        {
            return false;
        }

        var owned = await lists.GetForOwnerAsync(owner, cancellationToken);

        // Also what a redelivery finds: removal is idempotent, but there is no point loading and
        // saving nothing to prove it.
        if (owned.Count == 0)
        {
            return false;
        }

        foreach (var list in owned)
        {
            lists.Remove(list);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return true;
    }

    public Task<IReadOnlyList<UserId>> ListDataOwnerIdsAsync(
        CancellationToken cancellationToken = default) =>
        queries.ListDataOwnerIdsAsync(cancellationToken);
}

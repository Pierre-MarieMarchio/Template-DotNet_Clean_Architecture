using AppTemplate.Application.Core.Common.Events;
using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Application.Features.Reminders.Ports.ReminderQueries;
using AppTemplate.Domain.Core.Common.Primitives;
using AppTemplate.Domain.Features.Reminders.Repositories;

namespace AppTemplate.Application.Features.Reminders.Services.Consumers.AccountDeleted;

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

        if (await accounts.ExistsAsync(owner, cancellationToken))
        {
            return false;
        }

        var owned = await reminders.GetForOwnerAsync(owner, cancellationToken);

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

using AppTemplate.Domain.Core.Common.Primitives;
using AppTemplate.Domain.Features.Reminders.Entities;

namespace AppTemplate.Domain.Features.Reminders.Repositories;

/// <summary>
/// One method per thing a use case actually needs, like every repository here. Nothing here
/// writes: <c>Add</c> and <c>Remove</c> stage, and <c>IUnitOfWork</c> is what commits.
/// </summary>
public interface IReminderRepository
{
    /// <returns>The reminder, or <c>null</c> when no reminder has that id.</returns>
    Task<Reminder?> GetAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// Every reminder attached to one item, whatever its state; used on completion or removal, so
    /// the caller decides what a cancellation means rather than the store guessing.
    /// </summary>
    Task<IReadOnlyList<Reminder>> GetForTodoItemAsync(Guid todoItemId, CancellationToken cancellationToken);

    /// <summary>Reminders that are due, not yet retired and not waiting out a failed attempt, in
    /// the order they became eligible — <c>DueAt</c>, or <c>NextAttemptAt</c> for one that has
    /// failed — capped at <paramref name="batchSize"/>; the order keeps a reminder that always
    /// fails from holding the front of the batch. Exclusivity is not this method's job: the
    /// caller's leader lease and the store's concurrency token decide between two hosts.</summary>
    Task<IReadOnlyList<Reminder>> GetDueAsync(
        DateTimeOffset asOf,
        int batchSize,
        CancellationToken cancellationToken);

    /// <summary>
    /// Every reminder one owner holds, whatever its state; loaded as aggregates, because the
    /// account-deleted policy removes through the aggregate, not around it.
    /// </summary>
    Task<IReadOnlyList<Reminder>> GetForOwnerAsync(
        UserId ownerId,
        CancellationToken cancellationToken);

    void Add(Reminder reminder);

    void Remove(Reminder reminder);
}

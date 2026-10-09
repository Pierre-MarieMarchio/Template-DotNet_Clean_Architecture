using AppTemplate.Domain.Features.Reminders.Entities;

namespace AppTemplate.Domain.Features.Reminders.Repositories;

/// <summary>
/// Deliberately not generic, for the same reason as every repository here: one method per thing a
/// use case actually needs. Two of these have no counterpart on the to-do list's repository, which
/// is what an aggregate-shaped contract looks like once there is more than one aggregate.
/// <para>
/// Nothing here writes: <c>Add</c> and <c>Remove</c> stage, and <c>IUnitOfWork</c> is what commits.
/// </para>
/// </summary>
public interface IReminderRepository
{
    /// <returns>The reminder, or <c>null</c> when no reminder has that id.</returns>
    Task<Reminder?> GetAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// Every reminder attached to one item, whatever its state. Used when the item is completed or
    /// removed, so the caller decides what a cancellation means rather than the store guessing.
    /// </summary>
    Task<IReadOnlyList<Reminder>> GetForTodoItemAsync(Guid todoItemId, CancellationToken cancellationToken);

    /// <summary>
    /// Reminders that are due, not yet retired and not waiting out a failed attempt, in the order
    /// they became eligible — <c>DueAt</c>, or <c>NextAttemptAt</c> for one that has failed — capped
    /// at <paramref name="batchSize"/> so one pass cannot load an unbounded backlog into memory.
    /// <para>
    /// The order is what keeps a reminder that always fails from holding the front of the batch: a
    /// failure moves it behind everything that has been waiting since before its retry time, so it
    /// cannot keep later reminders out.
    /// </para>
    /// <para>
    /// Claiming is the caller's job: this only says what is eligible. Two hosts running this at the
    /// same instant will see the same rows, and it is <c>Reminder.TryClaim</c> plus the store's
    /// concurrency token that decide which of them may act.
    /// </para>
    /// </summary>
    Task<IReadOnlyList<Reminder>> GetDueAsync(
        DateTimeOffset asOf,
        int batchSize,
        CancellationToken cancellationToken);

    void Add(Reminder reminder);

    void Remove(Reminder reminder);
}

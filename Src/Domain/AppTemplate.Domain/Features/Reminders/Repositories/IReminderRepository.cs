using AppTemplate.Domain.Features.Reminders.Entities;

namespace AppTemplate.Domain.Features.Reminders.Repositories;

/// <summary>
/// Deliberately not generic, for the same reason as every repository here: one method per thing a
/// use case actually needs. Two of these have no counterpart on the to-do list's repository, which
/// is what an aggregate-shaped contract looks like once there is more than one aggregate.
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
    /// Reminders that are due and not yet retired, oldest first, capped at
    /// <paramref name="batchSize"/> so one pass cannot load an unbounded backlog into memory.
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

    /// <summary>Stages a new reminder for insertion.</summary>
    void Add(Reminder reminder);

    /// <summary>Stages a reminder for deletion.</summary>
    void Remove(Reminder reminder);
}

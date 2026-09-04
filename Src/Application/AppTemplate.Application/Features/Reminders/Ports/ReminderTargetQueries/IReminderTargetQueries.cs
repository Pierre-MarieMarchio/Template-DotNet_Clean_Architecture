namespace AppTemplate.Application.Features.Reminders.Ports.ReminderTargetQueries;

/// <summary>
/// A read projection onto <c>TodoItem</c>: no aggregate is loaded to answer "is this item done", so
/// this lives beside the other application ports rather than in the Domain, where only a contract
/// that loads an aggregate belongs.
/// </summary>
public interface IReminderTargetQueries
{
    /// <returns>
    /// Whether each item in <paramref name="todoItemIds"/> is completed. An id absent from the
    /// result means the item no longer exists at all — deleted, not completed. A caller cancels a
    /// reminder either way, since neither lets it legitimately fire, but the two are not the same
    /// event: only "completed" is a divergence worth counting — see
    /// <c>IReminderDiagnostics.RecordMissedCancellation</c>. "Absent" is the mechanism working as
    /// intended, since deleting an item raises no domain event to have missed in the first place.
    /// </returns>
    Task<IReadOnlyDictionary<Guid, bool>> GetCompletionStatesAsync(
        IReadOnlyList<Guid> todoItemIds,
        CancellationToken cancellationToken = default);
}

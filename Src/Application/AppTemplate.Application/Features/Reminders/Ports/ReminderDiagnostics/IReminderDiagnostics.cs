namespace AppTemplate.Application.Features.Reminders.Ports.ReminderDiagnostics;

/// <summary>
/// A counter, not a log: how many times firing found a reminder still <c>Pending</c> against an
/// item that was already completed. That count is exactly the number of completion events that
/// never reached <c>CancelRemindersOnTodoItemCompletedConsumer</c>. Delivery is in-process, after
/// commit, at most once, with no outbox; this counter is what makes the accepted loss observable
/// rather than silent.
/// </summary>
public interface IReminderDiagnostics
{
    void RecordMissedCancellation();
}

namespace AppTemplate.Application.Features.Reminders.Ports.ReminderDiagnostics;

/// <summary>
/// A counter, not a log: how many times firing found a reminder still <c>Pending</c> against an
/// item that was already completed. That count is exactly the number of completion events that
/// never reached <c>CancelRemindersOnTodoItemCompletedConsumer</c> — the mechanism
/// <c>docs/adr/0017</c> accepts as in-process and unretried, made observable instead of silent.
/// </summary>
public interface IReminderDiagnostics
{
    void RecordMissedCancellation();
}

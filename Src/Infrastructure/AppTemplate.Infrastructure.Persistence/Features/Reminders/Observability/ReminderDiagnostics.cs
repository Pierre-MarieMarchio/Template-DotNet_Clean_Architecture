using System.Diagnostics.Metrics;
using AppTemplate.Application.Features.Reminders.Ports.ReminderDiagnostics;

namespace AppTemplate.Infrastructure.Persistence.Features.Reminders.Observability;

/// <summary>The adapter for <see cref="IReminderDiagnostics"/>: one counter,
/// <c>reminders.missed_cancellations</c>, incremented once per call to
/// <see cref="IReminderDiagnostics.RecordMissedCancellation"/> — a completion (or deletion) whose
/// cancellation never reached <c>CancelRemindersOnTodoItemCompletedConsumer</c>. The count is what
/// lets a deployment alert on the accepted gap in event delivery; <c>SECURITY.md</c> cites the
/// counter by name, so the instrument name is not free to drift from it.</summary>
/// <remarks>Static fields, as a <see cref="Meter"/> is meant to be created once per process. Exported
/// by the API's telemetry registration — the only place that calls
/// <see cref="IReminderDiagnostics.RecordMissedCancellation"/> — under
/// <see cref="PersistenceModule.RemindersMeterName"/>, the one constant both sides read.</remarks>
internal sealed class ReminderDiagnostics : IReminderDiagnostics
{
    private static readonly Meter _meter = new(PersistenceModule.RemindersMeterName);

    private static readonly Counter<long> _missedCancellations = _meter.CreateCounter<long>(
        "apptemplate.reminders.missed_cancellations",
        unit: "{reminder}",
        // One sentence, because SECURITY.md cites this name as the number an
        // operator watches: a due reminder whose target is already completed is, by construction,
        // a reminder the consumer should have cancelled — so this counts exactly the completion
        // events that never reached it.
        description:
            "The number of lost completion events: a due reminder whose target is already " +
            "completed is, by construction, a reminder CancelRemindersOnTodoItemCompletedConsumer " +
            "should have cancelled.");

    public void RecordMissedCancellation() => _missedCancellations.Add(1);
}

using System.Diagnostics.Metrics;
using AppTemplate.Application.Features.Reminders.Ports.ReminderDiagnostics;

namespace AppTemplate.Infrastructure.Persistence.Features.Reminders.Observability;

/// <summary>
/// The adapter for <see cref="IReminderDiagnostics"/>: one counter,
/// <c>reminders.missed_cancellations</c>, incremented once per call to
/// <see cref="IReminderDiagnostics.RecordMissedCancellation"/>. Every increment is a completion (or
/// deletion) whose cancellation never reached <c>CancelRemindersOnTodoItemCompletedConsumer</c>,
/// and that count is what lets a deployment alert on the accepted gap in event delivery going
/// non-zero instead of discovering it by reading logs after the fact. <c>SECURITY.md</c> cites this
/// counter by name, so the instrument name below is not free to drift from it.
/// </summary>
/// <remarks>
/// Static fields, the same shape as <c>MaintenanceInstruments</c> in <c>AppTemplate.Worker</c>: a
/// <see cref="Meter"/> is meant to be created once per process, not once per scope. Exported by
/// <c>AppTemplate.Worker</c>'s own OpenTelemetry setup — the only host that ever calls
/// <see cref="IReminderDiagnostics.RecordMissedCancellation"/> — which is why the meter name below
/// has to be kept in sync with the literal <c>AddMeter</c> call there rather than shared as a
/// compile-time constant: this class is internal, and Worker cannot reference it across the
/// assembly boundary just to read one string.
/// </remarks>
internal sealed class ReminderDiagnostics : IReminderDiagnostics
{
    private static readonly Meter _meter = new("AppTemplate.Reminders");

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

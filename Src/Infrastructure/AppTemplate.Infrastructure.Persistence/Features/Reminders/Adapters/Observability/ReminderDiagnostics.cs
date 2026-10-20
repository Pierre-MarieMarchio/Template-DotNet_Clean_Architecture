using System.Diagnostics.Metrics;
using AppTemplate.Application.Features.Reminders.Ports.ReminderDiagnostics;

namespace AppTemplate.Infrastructure.Persistence.Features.Reminders.Adapters.Observability;

internal sealed class ReminderDiagnostics : IReminderDiagnostics
{
    private static readonly Meter _meter = new(PersistenceModule.RemindersMeterName);

    private static readonly Counter<long> _missedCancellations = _meter.CreateCounter<long>(
        "apptemplate.reminders.missed_cancellations",
        unit: "{reminder}",
        description:
            "The number of lost completion events: a due reminder whose target is already " +
            "completed is, by construction, a reminder CancelRemindersOnTodoItemCompletedConsumer " +
            "should have cancelled.");

    public void RecordMissedCancellation() => _missedCancellations.Add(1);
}

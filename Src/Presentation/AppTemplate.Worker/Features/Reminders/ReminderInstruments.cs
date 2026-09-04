using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace AppTemplate.Worker.Features.Reminders;

/// <summary>
/// The signal that the reminder loop ran at all, independent of whether anything was due.
/// <para>
/// The loop already argued for this in prose: its own comment says a pass that notified nobody for
/// days because the due-date query stopped matching must look different from a healthy pass that
/// simply had nothing due. It said so with a log line, which an operator can read but cannot alert
/// on going flat. <see cref="Iterations"/> is the same statement as a series.
/// </para>
/// </summary>
internal static class ReminderInstruments
{
    /// <summary>Shared by the <see cref="ActivitySource"/> and the <see cref="Meter"/> below, and by
    /// <c>WorkerObservabilityExtensions</c>'s own <c>AddSource</c>/<c>AddMeter</c> calls — one name,
    /// so the two can never drift apart.</summary>
    public const string Name = "AppTemplate.Worker.Reminders";

    public static readonly ActivitySource ActivitySource = new(Name);

    private static readonly Meter _meter = new(Name);

    /// <summary>
    /// One per pass, tagged by outcome — never gated on how many reminders were due. <c>disabled</c>
    /// is one of the outcomes: a loop switched off by configuration is a running loop that decided
    /// to do nothing, and an operator reading a flat line needs to know which of the two it is.
    /// </summary>
    public static readonly Counter<long> Iterations = _meter.CreateCounter<long>(
        "apptemplate.worker.reminders.iterations",
        unit: "{iteration}",
        description: "Reminder passes that ran, tagged by outcome (success, failure, exception, disabled).");

    /// <summary>
    /// Reminders actually notified. Zero is a valid and common measurement, which is exactly why it
    /// cannot stand in for <see cref="Iterations"/>.
    /// </summary>
    public static readonly Counter<long> Notified = _meter.CreateCounter<long>(
        "apptemplate.worker.reminders.notified",
        unit: "{reminder}",
        description: "Due reminders the loop notified.");
}

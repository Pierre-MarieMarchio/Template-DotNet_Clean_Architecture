using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace AppTemplate.Api.Features.Reminders.Jobs;

internal static class ReminderInstruments
{
    public const string Name = "AppTemplate.Jobs.Reminders";

    public static readonly ActivitySource ActivitySource = new(Name);

    private static readonly Meter _meter = new(Name);

    public static readonly Counter<long> Iterations = _meter.CreateCounter<long>(
        "apptemplate.jobs.reminders.iterations",
        unit: "{iteration}",
        description: "Reminder passes that ran, tagged by outcome (success, failure, exception, disabled).");

    public static readonly Counter<long> Notified = _meter.CreateCounter<long>(
        "apptemplate.jobs.reminders.notified",
        unit: "{reminder}",
        description: "Due reminders the loop notified.");
}

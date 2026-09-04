using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace AppTemplate.Worker.Features.Maintenance;

/// <summary>
/// The one signal that a maintenance iteration ran at all, independent of whether it
/// purged anything. A task whose query silently stops matching purges 0 rows every
/// hour and is otherwise indistinguishable from a healthy task with nothing to do —
/// a count of what was removed cannot tell the two apart, and only
/// <see cref="Iterations"/> going flat can.
/// </summary>
internal static class MaintenanceInstruments
{
    /// <summary>Shared by the <see cref="ActivitySource"/> and the <see cref="Meter"/> below, and
    /// by <c>WorkerObservabilityExtensions</c>'s own <c>AddSource</c>/<c>AddMeter</c> calls — one name, so the
    /// two can never drift apart.</summary>
    public const string Name = "AppTemplate.Worker.Maintenance";

    public static readonly ActivitySource ActivitySource = new(Name);

    private static readonly Meter _meter = new(Name);

    /// <summary>One per task per iteration, tagged by task name and outcome — never gated on the count.</summary>
    public static readonly Counter<long> Iterations = _meter.CreateCounter<long>(
        "apptemplate.worker.maintenance.iterations",
        unit: "{iteration}",
        description: "Maintenance tasks that ran, tagged by task and outcome (success, failure, exception).");

    /// <summary>Rows actually removed, tagged by task name. Zero is a valid, and common, measurement.</summary>
    public static readonly Counter<long> Purged = _meter.CreateCounter<long>(
        "apptemplate.worker.maintenance.purged",
        unit: "{row}",
        description: "Rows purged per maintenance task.");
}

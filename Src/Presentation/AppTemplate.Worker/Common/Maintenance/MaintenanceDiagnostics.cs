using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace AppTemplate.Worker.Common.Maintenance;

/// <summary>
/// The one signal that a maintenance iteration ran at all, independent of whether it purged
/// anything. Before this, a task that purged 0 rows every hour for weeks — because its query was
/// silently wrong — was indistinguishable from a healthy task that simply had nothing to do: the
/// log line only fired when the count was positive. An operator can alert on
/// <see cref="Iterations"/> going flat; nothing before could tell them a purge had died.
/// </summary>
internal static class MaintenanceDiagnostics
{
    /// <summary>Shared by the <see cref="ActivitySource"/> and the <see cref="Meter"/> below, and
    /// by <c>WorkerObservability</c>'s own <c>AddSource</c>/<c>AddMeter</c> calls — one name, so the
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

using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace AppTemplate.Api.Features.Maintenance.Jobs;

internal static class MaintenanceInstruments
{
    public const string Name = "AppTemplate.Jobs.Maintenance";

    public static readonly ActivitySource ActivitySource = new(Name);

    private static readonly Meter _meter = new(Name);

    public static readonly Counter<long> Iterations = _meter.CreateCounter<long>(
        "apptemplate.jobs.maintenance.iterations",
        unit: "{iteration}",
        description: "Maintenance tasks that ran, tagged by task and outcome (success, failure, exception).");

    public static readonly Counter<long> Purged = _meter.CreateCounter<long>(
        "apptemplate.jobs.maintenance.purged",
        unit: "{row}",
        description: "Rows purged per maintenance task.");
}

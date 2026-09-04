using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace AppTemplate.Worker.Features.Files;

/// <summary>
/// The signal that the file feature's sweeps ran at all, independent of whether they found anything.
/// <see cref="Iterations"/> is the one an alert watches: both sweeps legitimately report zero for
/// long stretches, so a count of what they removed can never distinguish a healthy quiet system from
/// a loop that died weeks ago — only "the pass happened" can.
/// <para>
/// The two volume counters are separate rather than one counter tagged by task, unlike
/// <c>MaintenanceInstruments.Purged</c>, because they do not measure the same thing: one counts rows
/// in a table and the other counts objects in a store. A single series would invite a dashboard to
/// add them up, and the sum would mean nothing.
/// </para>
/// </summary>
internal static class FileInstruments
{
    /// <summary>Shared by the <see cref="ActivitySource"/> and the <see cref="Meter"/> below, and by
    /// <c>WorkerObservabilityExtensions</c>'s own <c>AddSource</c>/<c>AddMeter</c> calls — one name,
    /// so the two can never drift apart.</summary>
    public const string Name = "AppTemplate.Worker.Files";

    public static readonly ActivitySource ActivitySource = new(Name);

    private static readonly Meter _meter = new(Name);

    /// <summary>One per sweep per pass, tagged by task and outcome — never gated on the count. A pass
    /// a configuration flag turned off counts as <c>disabled</c> rather than not counting at all, so
    /// an alert written on "this loop stopped" has to read <c>outcome != "disabled"</c>.</summary>
    public static readonly Counter<long> Iterations = _meter.CreateCounter<long>(
        "apptemplate.worker.files.iterations",
        unit: "{iteration}",
        description: "File sweeps that ran, tagged by task and outcome (success, failure, exception, disabled).");

    /// <summary>Registrations removed because their deposit never arrived. Zero is a valid, and common, measurement.</summary>
    public static readonly Counter<long> RegistrationsPurged = _meter.CreateCounter<long>(
        "apptemplate.worker.files.registrations_purged",
        unit: "{row}",
        description: "Pending stored-file registrations removed for having never been deposited against.");

    /// <summary>
    /// Objects deleted from the store because no row named them. The number an operator reads
    /// against the storage bill: it is the only place the bytes of a deleted file are ever given
    /// back, so a long run of zeroes alongside a healthy delete rate means the sweep is finding
    /// nothing it should be finding.
    /// </summary>
    public static readonly Counter<long> ObjectsReclaimed = _meter.CreateCounter<long>(
        "apptemplate.worker.files.objects_reclaimed",
        unit: "{object}",
        description: "Stored objects deleted for being referenced by no live row.");

    /// <summary>
    /// Deposits the inspection loop reached a verdict on — released or refused, both counted here.
    /// </summary>
    /// <remarks>
    /// This is the one counter to alert on, because it is the only loop whose silence a user feels:
    /// the other two leak storage when they stop, this one makes every upload permanently
    /// unreadable. A rate of zero while files are being deposited is not a quiet system, it is a
    /// broken one.
    /// </remarks>
    public static readonly Counter<long> DepositsInspected = _meter.CreateCounter<long>(
        "apptemplate.worker.files.deposits_inspected",
        unit: "{file}",
        description: "Deposited files the inspection loop decided on, released or refused.");
}

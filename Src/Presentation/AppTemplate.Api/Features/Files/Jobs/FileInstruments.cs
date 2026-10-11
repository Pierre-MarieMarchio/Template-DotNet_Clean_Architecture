using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace AppTemplate.Api.Features.Files.Jobs;

/// <summary>
/// Signals that the file passes ran at all, whether or not they found anything. Alert on
/// <see cref="Iterations"/>: the volume counters legitimately stay at zero for long stretches.
/// </summary>
internal static class FileInstruments
{
    /// <summary>Shared by the <see cref="ActivitySource"/>, the <see cref="Meter"/> and <c>BackgroundJobsTelemetry</c>'s registration.</summary>
    public const string Name = "AppTemplate.Jobs.Files";

    public static readonly ActivitySource ActivitySource = new(Name);

    private static readonly Meter _meter = new(Name);

    /// <summary>One per pass, tagged by task and outcome. A pass a flag turned off counts as <c>disabled</c>, so alert on <c>outcome != "disabled"</c>.</summary>
    public static readonly Counter<long> Iterations = _meter.CreateCounter<long>(
        "apptemplate.jobs.files.iterations",
        unit: "{iteration}",
        description: "File sweeps that ran, tagged by task and outcome (success, failure, exception, disabled).");

    /// <summary>Registrations removed because their deposit never arrived.</summary>
    public static readonly Counter<long> RegistrationsPurged = _meter.CreateCounter<long>(
        "apptemplate.jobs.files.registrations_purged",
        unit: "{row}",
        description: "Pending stored-file registrations removed for having never been deposited against.");

    /// <summary>
    /// Objects deleted from the store because no row named them; the only place the bytes of a deleted
    /// file are given back.
    /// </summary>
    public static readonly Counter<long> ObjectsReclaimed = _meter.CreateCounter<long>(
        "apptemplate.jobs.files.objects_reclaimed",
        unit: "{object}",
        description: "Stored objects deleted for being referenced by no live row.");

    /// <summary>
    /// Deposits the inspection pass reached a verdict on, released or refused. Alert on a zero rate
    /// while files are being deposited: every upload would stay unreadable.
    /// </summary>
    public static readonly Counter<long> DepositsInspected = _meter.CreateCounter<long>(
        "apptemplate.jobs.files.deposits_inspected",
        unit: "{file}",
        description: "Deposited files the inspection loop decided on, released or refused.");
}

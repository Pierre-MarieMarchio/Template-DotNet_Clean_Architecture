using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace AppTemplate.Api.Features.Files.Jobs;

internal static class FileInstruments
{
    public const string Name = "AppTemplate.Jobs.Files";

    public static readonly ActivitySource ActivitySource = new(Name);

    private static readonly Meter _meter = new(Name);

    public static readonly Counter<long> Iterations = _meter.CreateCounter<long>(
        "apptemplate.jobs.files.iterations",
        unit: "{iteration}",
        description: "File sweeps that ran, tagged by task and outcome (success, failure, exception, disabled).");

    public static readonly Counter<long> RegistrationsPurged = _meter.CreateCounter<long>(
        "apptemplate.jobs.files.registrations_purged",
        unit: "{row}",
        description: "Pending stored-file registrations removed for having never been deposited against.");

    public static readonly Counter<long> ObjectsReclaimed = _meter.CreateCounter<long>(
        "apptemplate.jobs.files.objects_reclaimed",
        unit: "{object}",
        description: "Stored objects deleted for being referenced by no live row.");

    public static readonly Counter<long> DepositsInspected = _meter.CreateCounter<long>(
        "apptemplate.jobs.files.deposits_inspected",
        unit: "{file}",
        description: "Deposited files the inspection loop decided on, released or refused.");
}

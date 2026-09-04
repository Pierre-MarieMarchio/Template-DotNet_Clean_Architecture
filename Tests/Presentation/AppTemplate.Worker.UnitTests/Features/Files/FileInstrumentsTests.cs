using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using AppTemplate.Application.Features.Files.UseCases.Commands.PurgeAbandonedRegistrations;
using AppTemplate.Application.Features.Files.UseCases.Commands.ReclaimOrphanedContent;
using AppTemplate.Worker.Features.Files;
using AppTemplate.Worker.UnitTests.TestSupport;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using Xunit;

namespace AppTemplate.Worker.UnitTests.Features.Files;

/// <summary>
/// Proves the metric the file loops emit carries the case that would otherwise stay invisible: a
/// sweep that removed nothing. That is the normal state of both of these in a healthy system, so a
/// count of what they removed can never say whether they ran — only
/// <c>apptemplate.worker.files.iterations</c> can, and it is what an alert on "a sweep stopped"
/// would watch.
/// </summary>
public sealed class FileInstrumentsTests
{
    private static readonly TimeSpan _tinyInterval = TimeSpan.FromMilliseconds(20);

    [Fact]
    public async Task RunningASweep_RecordsAnIteration_EvenWhenNothingWasRemoved()
    {
        var purge = new FakeAbandonedRegistrationPurge(count: 0);
        var sweep = new FakeOrphanedContentSweep(count: 0);

        // Concurrent, not a List: this host runs its two loops at the same time, so measurements
        // arrive on two threads at once, and every other test class in this assembly is recording to
        // the same static meter in parallel. A List loses items under that and the loss is silent —
        // it cost one intermittent red here before the collection was changed.
        var purgedMeasurements = new ConcurrentQueue<(long Value, string? Task)>();
        var reclaimedMeasurements = new ConcurrentQueue<(long Value, string? Task)>();
        var iterationMeasurements = new ConcurrentQueue<(long Value, string? Task, string? Outcome)>();

        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == "AppTemplate.Worker.Files")
            {
                l.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((instrument, measurement, tags, _) =>
        {
            var tagArray = tags.ToArray();
            string? task = tagArray.FirstOrDefault(t => t.Key == "task").Value as string;

            if (instrument.Name == "apptemplate.worker.files.registrations_purged")
            {
                purgedMeasurements.Enqueue((measurement, task));
            }
            else if (instrument.Name == "apptemplate.worker.files.objects_reclaimed")
            {
                reclaimedMeasurements.Enqueue((measurement, task));
            }
            else if (instrument.Name == "apptemplate.worker.files.iterations")
            {
                string? outcome = tagArray.FirstOrDefault(t => t.Key == "outcome").Value as string;
                iterationMeasurements.Enqueue((measurement, task, outcome));
            }
        });
        listener.Start();

        var services = new ServiceCollection();
        services.AddScoped<IPurgeAbandonedRegistrationsUseCase>(_ => purge);
        services.AddScoped<IReclaimOrphanedContentUseCase>(_ => sweep);
        using var provider = services.BuildServiceProvider();

        var options = new FileWorkerOptions
        {
            PurgeAbandonedRegistrationsInterval = _tinyInterval,
            PurgeAbandonedRegistrationsEnabled = true,
            ReclaimOrphanedContentInterval = _tinyInterval,
            ReclaimOrphanedContentEnabled = true,
        };

        using var service = new FileBackgroundService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(options),
            NullLogger<FileBackgroundService>.Instance);

        await service.StartAsync(TestContext.Current.CancellationToken);
        await BackgroundServiceProbe.WaitUntilAsync(
            () => purge.CallCount >= 1 && sweep.CallCount >= 1,
            "both file sweeps to have run once");
        await service.StopAsync(TestContext.Current.CancellationToken);

        // No other fake in this project reports a success with zero removed, so a zero here can only
        // have come from this test's own pass, even if other tests share the static meter.
        purgedMeasurements.ShouldContain(m => m.Value == 0 && m.Task == "abandoned registrations");
        reclaimedMeasurements.ShouldContain(m => m.Value == 0 && m.Task == "orphaned content");
        iterationMeasurements.ShouldContain(m => m.Outcome == "success" && m.Task == "abandoned registrations");
        iterationMeasurements.ShouldContain(m => m.Outcome == "success" && m.Task == "orphaned content");
    }

    /// <summary>
    /// The other half of the same requirement: a sweep whose every pass throws still records that it
    /// ran. Without this the iterations counter would go flat exactly when the loop is broken, which
    /// is the one moment it exists for — an outage would look identical to a stopped process.
    /// </summary>
    [Fact]
    public async Task AFailingSweep_StillRecordsAnIteration()
    {
        var purge = new FakeAbandonedRegistrationPurge(count: 0);
        var sweep = new FakeOrphanedContentSweep(new InvalidOperationException("boom"));

        var iterationMeasurements = new ConcurrentQueue<(string? Task, string? Outcome)>();

        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == "AppTemplate.Worker.Files"
                && instrument.Name == "apptemplate.worker.files.iterations")
            {
                l.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((_, _, tags, _) =>
        {
            var tagArray = tags.ToArray();
            iterationMeasurements.Enqueue((
                tagArray.FirstOrDefault(t => t.Key == "task").Value as string,
                tagArray.FirstOrDefault(t => t.Key == "outcome").Value as string));
        });
        listener.Start();

        var services = new ServiceCollection();
        services.AddScoped<IPurgeAbandonedRegistrationsUseCase>(_ => purge);
        services.AddScoped<IReclaimOrphanedContentUseCase>(_ => sweep);
        using var provider = services.BuildServiceProvider();

        var options = new FileWorkerOptions
        {
            PurgeAbandonedRegistrationsInterval = _tinyInterval,
            PurgeAbandonedRegistrationsEnabled = true,
            ReclaimOrphanedContentInterval = _tinyInterval,
            ReclaimOrphanedContentEnabled = true,
        };

        using var service = new FileBackgroundService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(options),
            NullLogger<FileBackgroundService>.Instance);

        await service.StartAsync(TestContext.Current.CancellationToken);
        await BackgroundServiceProbe.WaitUntilAsync(
            () => sweep.CallCount >= 1,
            "the orphan sweep to have thrown once");
        await service.StopAsync(TestContext.Current.CancellationToken);

        iterationMeasurements.ShouldContain(m => m.Task == "orphaned content" && m.Outcome == "exception");
    }

    /// <summary>
    /// The third case, and the one this feature needs most: a loop a configuration flag switched off
    /// still counts its passes, tagged <c>disabled</c>. Without it an operator reading a flat
    /// <c>apptemplate.worker.files.iterations</c> cannot tell a loop somebody turned off from one
    /// that died — and the inspection loop is the one whose death leaves every upload permanently
    /// unreadable. <c>ReminderBackgroundService</c> has counted its own disabled pass all along; this
    /// holds the file loops to the same reading.
    /// </summary>
    [Fact]
    public async Task ADisabledSweep_RecordsAnIteration_SoAFlatLineIsNotAmbiguous()
    {
        var iterationMeasurements = new ConcurrentQueue<(string? Task, string? Outcome)>();

        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == "AppTemplate.Worker.Files"
                && instrument.Name == "apptemplate.worker.files.iterations")
            {
                l.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((_, _, tags, _) =>
        {
            var tagArray = tags.ToArray();
            iterationMeasurements.Enqueue((
                tagArray.FirstOrDefault(t => t.Key == "task").Value as string,
                tagArray.FirstOrDefault(t => t.Key == "outcome").Value as string));
        });
        listener.Start();

        // No use case is registered at all: a disabled pass must never open a scope, so a loop that
        // reached for one would fail this test rather than quietly counting an exception.
        using var provider = new ServiceCollection().BuildServiceProvider();

        var options = new FileWorkerOptions
        {
            PurgeAbandonedRegistrationsInterval = _tinyInterval,
            PurgeAbandonedRegistrationsEnabled = false,
            ReclaimOrphanedContentInterval = _tinyInterval,
            ReclaimOrphanedContentEnabled = false,
            InspectDepositedFilesInterval = _tinyInterval,
            InspectDepositedFilesEnabled = false,
        };

        using var service = new FileBackgroundService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(options),
            NullLogger<FileBackgroundService>.Instance);

        await service.StartAsync(TestContext.Current.CancellationToken);
        await BackgroundServiceProbe.WaitUntilAsync(
            () => iterationMeasurements.Any(m => m.Task == "deposited files" && m.Outcome == "disabled"),
            "a disabled inspection pass to be counted");
        await service.StopAsync(TestContext.Current.CancellationToken);

        // ShouldContain, not an assertion over everything recorded: this static meter is shared with
        // every other test in the assembly and they run in parallel, so the queue holds their
        // measurements too.
        iterationMeasurements.ShouldContain(m => m.Task == "deposited files" && m.Outcome == "disabled");
    }
}

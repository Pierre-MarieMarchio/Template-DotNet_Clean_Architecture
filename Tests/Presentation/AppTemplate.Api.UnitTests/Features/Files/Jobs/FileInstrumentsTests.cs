using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using AppTemplate.Api.Common.Jobs;
using AppTemplate.Api.Features.Files.Jobs;
using AppTemplate.Api.UnitTests.TestSupport;
using AppTemplate.Application.Features.Files.UseCases.Commands.PurgeAbandonedRegistrations;
using AppTemplate.Application.Features.Files.UseCases.Commands.ReclaimOrphanedContent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using Xunit;

namespace AppTemplate.Api.UnitTests.Features.Files.Jobs;

public sealed class FileInstrumentsTests
{
    private static readonly TimeSpan _tinyInterval = TimeSpan.FromMilliseconds(20);

    [Fact]
    public async Task RunningASweep_RecordsAnIteration_EvenWhenNothingWasRemoved()
    {
        var purge = new FakeAbandonedRegistrationPurge(count: 0);
        var sweep = new FakeOrphanedContentSweep(count: 0);

        var purgedMeasurements = new ConcurrentQueue<(long Value, string? Task)>();
        var reclaimedMeasurements = new ConcurrentQueue<(long Value, string? Task)>();
        var iterationMeasurements = new ConcurrentQueue<(long Value, string? Task, string? Outcome)>();

        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == "AppTemplate.Jobs.Files")
            {
                l.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((instrument, measurement, tags, _) =>
        {
            var tagArray = tags.ToArray();
            string? task = tagArray.FirstOrDefault(t => t.Key == "task").Value as string;

            if (instrument.Name == "apptemplate.jobs.files.registrations_purged")
            {
                purgedMeasurements.Enqueue((measurement, task));
            }
            else if (instrument.Name == "apptemplate.jobs.files.objects_reclaimed")
            {
                reclaimedMeasurements.Enqueue((measurement, task));
            }
            else if (instrument.Name == "apptemplate.jobs.files.iterations")
            {
                string? outcome = tagArray.FirstOrDefault(t => t.Key == "outcome").Value as string;
                iterationMeasurements.Enqueue((measurement, task, outcome));
            }
        });
        listener.Start();

        var services = new ServiceCollection();
        services.AddBackgroundScope();
        services.AddScoped<IPurgeAbandonedRegistrationsUseCase>(_ => purge);
        services.AddScoped<IReclaimOrphanedContentUseCase>(_ => sweep);
        using var provider = services.BuildServiceProvider();

        var options = new JobsFilesOptions
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

        purgedMeasurements.ShouldContain(m => m.Value == 0 && m.Task == "abandoned registrations");
        reclaimedMeasurements.ShouldContain(m => m.Value == 0 && m.Task == "orphaned content");
        iterationMeasurements.ShouldContain(m => m.Outcome == "success" && m.Task == "abandoned registrations");
        iterationMeasurements.ShouldContain(m => m.Outcome == "success" && m.Task == "orphaned content");
    }

    [Fact]
    public async Task AFailingSweep_StillRecordsAnIteration()
    {
        var purge = new FakeAbandonedRegistrationPurge(count: 0);
        var sweep = new FakeOrphanedContentSweep(new InvalidOperationException("boom"));

        var iterationMeasurements = new ConcurrentQueue<(string? Task, string? Outcome)>();

        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == "AppTemplate.Jobs.Files"
                && instrument.Name == "apptemplate.jobs.files.iterations")
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
        services.AddBackgroundScope();
        services.AddScoped<IPurgeAbandonedRegistrationsUseCase>(_ => purge);
        services.AddScoped<IReclaimOrphanedContentUseCase>(_ => sweep);
        using var provider = services.BuildServiceProvider();

        var options = new JobsFilesOptions
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

    [Fact]
    public async Task ADisabledSweep_RecordsAnIteration_SoAFlatLineIsNotAmbiguous()
    {
        var iterationMeasurements = new ConcurrentQueue<(string? Task, string? Outcome)>();

        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == "AppTemplate.Jobs.Files"
                && instrument.Name == "apptemplate.jobs.files.iterations")
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

        using var provider = new ServiceCollection().AddBackgroundScope().BuildServiceProvider();

        var options = new JobsFilesOptions
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

        iterationMeasurements.ShouldContain(m => m.Task == "deposited files" && m.Outcome == "disabled");
    }
}

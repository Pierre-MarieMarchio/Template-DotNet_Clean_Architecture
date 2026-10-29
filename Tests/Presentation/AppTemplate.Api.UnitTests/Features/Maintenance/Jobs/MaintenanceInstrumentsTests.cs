using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using AppTemplate.Api.Features.Maintenance.Jobs;
using AppTemplate.Api.UnitTests.Features.BackgroundJobs.Jobs;
using Shouldly;
using Xunit;

namespace AppTemplate.Api.UnitTests.Features.Maintenance.Jobs;

public sealed class MaintenanceInstrumentsTests
{
    private static readonly TimeSpan _tinyInterval = TimeSpan.FromMilliseconds(20);

    [Fact]
    public async Task RunningTask_RecordsAnIteration_EvenWhenNothingWasPurged()
    {
        var idempotency = new FakeIdempotencyPurge(count: 0);
        //#if (localAuth)
        var refreshTokens = new FakeRefreshTokenPurge(count: 0);
        var securityEvents = new FakeSecurityEventPurge(count: 0);
        //#endif

        var purgedMeasurements = new ConcurrentQueue<(long Value, string? Task)>();
        var iterationMeasurements = new ConcurrentQueue<(long Value, string? Task, string? Outcome)>();

        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == "AppTemplate.Jobs.Maintenance")
            {
                l.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((instrument, measurement, tags, _) =>
        {
            var tagArray = tags.ToArray();
            string? task = tagArray.FirstOrDefault(t => t.Key == "task").Value as string;

            if (instrument.Name == "apptemplate.jobs.maintenance.purged")
            {
                purgedMeasurements.Enqueue((measurement, task));
            }
            else if (instrument.Name == "apptemplate.jobs.maintenance.iterations")
            {
                string? outcome = tagArray.FirstOrDefault(t => t.Key == "outcome").Value as string;
                iterationMeasurements.Enqueue((measurement, task, outcome));
            }
        });
        listener.Start();

        var options = new JobsMaintenanceOptions
        {
            Interval = _tinyInterval,
            PurgeExpiredIdempotencyKeysEnabled = true,
        };

        /*#if (localAuth)*/
        using var service = MaintenanceJobsHarness.Create(idempotency, refreshTokens, securityEvents, options);

        await service.StartAsync(TestContext.Current.CancellationToken);
        await BackgroundServiceProbe.WaitUntilAsync(
            () => idempotency.CallCount >= 1 && refreshTokens.CallCount >= 1 && securityEvents.CallCount >= 1,
            "all three purges to have run once");
        /*#else
        using var service = MaintenanceJobsHarness.Create(idempotency, options);

        await service.StartAsync(TestContext.Current.CancellationToken);
        await BackgroundServiceProbe.WaitUntilAsync(
            () => idempotency.CallCount >= 1,
            "the purge to have run once");
        #endif*/
        await service.StopAsync(TestContext.Current.CancellationToken);

        purgedMeasurements.ShouldContain(m => m.Value == 0);
        iterationMeasurements.ShouldContain(m => m.Outcome == "success" && m.Value == 1);
    }

    [Fact]
    public async Task DisabledTask_RecordsAnIteration_TaggedDisabled()
    {
        var iterationMeasurements = new ConcurrentQueue<(long Value, string? Task, string? Outcome)>();

        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == "AppTemplate.Jobs.Maintenance")
            {
                l.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((instrument, measurement, tags, _) =>
        {
            if (instrument.Name != "apptemplate.jobs.maintenance.iterations")
            {
                return;
            }

            var tagArray = tags.ToArray();

            iterationMeasurements.Enqueue((
                measurement,
                tagArray.FirstOrDefault(t => t.Key == "task").Value as string,
                tagArray.FirstOrDefault(t => t.Key == "outcome").Value as string));
        });
        listener.Start();

        /*#if (localAuth)*/
        var idempotency = new FakeIdempotencyPurge();
        var options = new JobsMaintenanceOptions
        {
            Interval = _tinyInterval,
            PurgeExpiredIdempotencyKeysEnabled = true,
        };

        using var service = MaintenanceJobsHarness.Create(
            idempotency,
            new FakeRefreshTokenPurge(),
            new FakeSecurityEventPurge(),
            options,
            refreshTokensEnabled: false,
            securityEventsEnabled: false);

        await service.StartAsync(TestContext.Current.CancellationToken);
        await BackgroundServiceProbe.WaitUntilAsync(
            () => idempotency.CallCount >= 1
                && iterationMeasurements.Any(m =>
                    m.Task == "expired refresh-token grants" && m.Outcome == "disabled" && m.Value == 1)
                && iterationMeasurements.Any(m =>
                    m.Task == "expired security events" && m.Outcome == "disabled" && m.Value == 1),
            "the enabled purge to have run once and both disabled ones to have recorded an iteration");
        await service.StopAsync(TestContext.Current.CancellationToken);

        iterationMeasurements.ShouldContain(m =>
            m.Task == "expired refresh-token grants" && m.Outcome == "disabled" && m.Value == 1);
        iterationMeasurements.ShouldContain(m =>
            m.Task == "expired security events" && m.Outcome == "disabled" && m.Value == 1);
        /*#else
        var options = new JobsMaintenanceOptions
        {
            Interval = _tinyInterval,
            PurgeExpiredIdempotencyKeysEnabled = false,
        };

        using var service = MaintenanceJobsHarness.Create(new FakeIdempotencyPurge(), options);

        await service.StartAsync(TestContext.Current.CancellationToken);
        await BackgroundServiceProbe.WaitUntilAsync(
            () => iterationMeasurements.Any(m =>
                m.Task == "expired idempotency keys" && m.Outcome == "disabled" && m.Value == 1),
            "the disabled purge to have recorded an iteration");
        await service.StopAsync(TestContext.Current.CancellationToken);
        #endif*/
    }

}

using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using AppTemplate.Api.Features.Maintenance.Jobs;
using AppTemplate.Api.UnitTests.TestSupport;
using Shouldly;
using Xunit;

namespace AppTemplate.Api.UnitTests.Features.Maintenance.Jobs;

/// <summary>
/// Proves the metric the maintenance loop emits carries the case that would otherwise stay
/// invisible: a task that purged nothing. The log line in <c>MaintenanceBackgroundService</c> is
/// unconditional for the same reason, but is not asserted here — <see cref="MaintenanceInstruments"/>
/// is the signal an alert would actually watch.
/// </summary>
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

        // Concurrent, not a List, for the reason FileInstrumentsTests already records: the listener
        // below is enabled on a static meter, so it receives every measurement any thread in this
        // assembly records to it — and MaintenanceBackgroundServiceTests runs the same loop in
        // parallel, recording to these same two counters every 20 ms. A List<T>.Add from two threads
        // can drop an item, and the one dropped here is this test's own zero, which reads as "the
        // metric never carried the case that would otherwise stay invisible".
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

        // No other fake in this project reports a success with zero purged, so a zero here can only
        // have come from this test's own iteration, even if other tests share the static meter.
        purgedMeasurements.ShouldContain(m => m.Value == 0);
        iterationMeasurements.ShouldContain(m => m.Outcome == "success" && m.Value == 1);
    }

    /// <summary>
    /// A task switched off by configuration still records an iteration, tagged as such: a flat
    /// series must not be the only thing an alert sees, because a purge switched off and a purge
    /// that died look identical from there.
    /// </summary>
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
            refreshTokensEnabled: false);

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
        // The one task this project has, switched off: nothing runs, so what the loop records is
        // the only sign it is alive.
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

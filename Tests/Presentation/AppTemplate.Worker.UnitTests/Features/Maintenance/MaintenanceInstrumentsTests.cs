using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using AppTemplate.Application.Features.Maintenance.UseCases.Commands.PurgeExpiredIdempotencyKeys;
using AppTemplate.Application.Features.Maintenance.UseCases.Commands.PurgeExpiredRefreshTokens;
using AppTemplate.Worker.Features.Maintenance;
using AppTemplate.Worker.UnitTests.TestSupport;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using Xunit;

namespace AppTemplate.Worker.UnitTests.Features.Maintenance;

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
        var refreshTokens = new FakeRefreshTokenPurge(count: 0);

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
            if (instrument.Meter.Name == "AppTemplate.Worker.Maintenance")
            {
                l.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((instrument, measurement, tags, _) =>
        {
            var tagArray = tags.ToArray();
            string? task = tagArray.FirstOrDefault(t => t.Key == "task").Value as string;

            if (instrument.Name == "apptemplate.worker.maintenance.purged")
            {
                purgedMeasurements.Enqueue((measurement, task));
            }
            else if (instrument.Name == "apptemplate.worker.maintenance.iterations")
            {
                string? outcome = tagArray.FirstOrDefault(t => t.Key == "outcome").Value as string;
                iterationMeasurements.Enqueue((measurement, task, outcome));
            }
        });
        listener.Start();

        var services = new ServiceCollection();
        services.AddScoped<IPurgeExpiredIdempotencyKeysUseCase>(_ => idempotency);
        services.AddScoped<IPurgeExpiredRefreshTokensUseCase>(_ => refreshTokens);
        using var provider = services.BuildServiceProvider();

        var options = new MaintenanceWorkerOptions
        {
            Interval = _tinyInterval,
            PurgeExpiredIdempotencyKeysEnabled = true,
            PurgeExpiredRefreshTokensEnabled = true,
        };

        using var service = new MaintenanceBackgroundService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(options),
            NullLogger<MaintenanceBackgroundService>.Instance);

        await service.StartAsync(TestContext.Current.CancellationToken);
        await BackgroundServiceProbe.WaitUntilAsync(
            () => idempotency.CallCount >= 1 && refreshTokens.CallCount >= 1,
            "both purges to have run once");
        await service.StopAsync(TestContext.Current.CancellationToken);

        // No other fake in this project reports a success with zero purged, so a zero here can only
        // have come from this test's own iteration, even if other tests share the static meter.
        purgedMeasurements.ShouldContain(m => m.Value == 0);
        iterationMeasurements.ShouldContain(m => m.Outcome == "success" && m.Value == 1);
    }

}

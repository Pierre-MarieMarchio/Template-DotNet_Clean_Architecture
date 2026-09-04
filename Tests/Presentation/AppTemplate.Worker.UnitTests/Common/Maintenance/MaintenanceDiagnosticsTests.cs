using System.Diagnostics.Metrics;
using AppTemplate.Application.Common;
using AppTemplate.Application.Features.Maintenance.UseCases.Commands;
using AppTemplate.Worker.Common.Maintenance;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using Xunit;

namespace AppTemplate.Worker.UnitTests.Common.Maintenance;

/// <summary>
/// Proves the metric the maintenance loop now emits actually carries the case that used to be
/// invisible: a task that purged nothing. The log line in <c>MaintenanceBackgroundService</c> is
/// unconditional for the same reason, but is not asserted here — <see cref="MaintenanceDiagnostics"/>
/// is the signal an alert would actually watch.
/// </summary>
public sealed class MaintenanceDiagnosticsTests
{
    private static readonly TimeSpan _tinyInterval = TimeSpan.FromMilliseconds(20);

    [Fact]
    public async Task RunningTask_RecordsAnIteration_EvenWhenNothingWasPurged()
    {
        var idempotency = new FakeIdempotencyPurge(count: 0);
        var refreshTokens = new FakeRefreshTokenPurge(count: 0);

        var purgedMeasurements = new List<(long Value, string? Task)>();
        var iterationMeasurements = new List<(long Value, string? Task, string? Outcome)>();

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
                purgedMeasurements.Add((measurement, task));
            }
            else if (instrument.Name == "apptemplate.worker.maintenance.iterations")
            {
                string? outcome = tagArray.FirstOrDefault(t => t.Key == "outcome").Value as string;
                iterationMeasurements.Add((measurement, task, outcome));
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
        await WaitUntilAsync(() => idempotency.CallCount >= 1 && refreshTokens.CallCount >= 1);
        await service.StopAsync(TestContext.Current.CancellationToken);

        // No other fake in this project reports a success with zero purged, so a zero here can only
        // have come from this test's own iteration, even if other tests share the static meter.
        purgedMeasurements.ShouldContain(m => m.Value == 0);
        iterationMeasurements.ShouldContain(m => m.Outcome == "success" && m.Value == 1);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        while (!condition())
        {
            timeout.Token.ThrowIfCancellationRequested();
            await Task.Delay(TimeSpan.FromMilliseconds(10), timeout.Token);
        }
    }
}

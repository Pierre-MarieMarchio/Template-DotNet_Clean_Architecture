using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using AppTemplate.Api.Common.Jobs;
using AppTemplate.Api.Features.Reminders.Jobs;
using AppTemplate.Api.UnitTests.TestSupport;
using AppTemplate.Application.Features.Reminders.UseCases.Commands.FireDueReminders;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using Xunit;

namespace AppTemplate.Api.UnitTests.Features.Reminders.Jobs;

public sealed class ReminderInstrumentsTests
{
    private static readonly TimeSpan _tinyInterval = TimeSpan.FromMilliseconds(20);

    [Fact]
    public async Task RunningPass_RecordsAnIteration_EvenWhenNobodyWasNotified()
    {
        var useCase = new FakeFireDueRemindersUseCase(count: 0);
        var iterations = Recorder();

        using MeterListener listener = Listen(iterations);

        await RunAsync(useCase, enabled: true, () => useCase.CallCount >= 1, "one reminder pass");

        iterations.ShouldContain(m => m.Outcome == "success" && m.Value == 1);
    }

    [Fact]
    public async Task DisabledPass_RecordsAnIteration_SoAFlatLineIsNotAmbiguous()
    {
        var useCase = new FakeFireDueRemindersUseCase();
        var iterations = Recorder();

        using MeterListener listener = Listen(iterations);

        await RunAsync(
            useCase,
            enabled: false,
            () => iterations.Any(m => m.Outcome == "disabled"),
            "a disabled pass to be counted");

        iterations.ShouldContain(m => m.Outcome == "disabled" && m.Value == 1);
        useCase.CallCount.ShouldBe(0);
    }

    [Fact]
    public async Task ThrowingPass_RecordsAnIteration_TaggedException()
    {
        var useCase = new FakeFireDueRemindersUseCase(new InvalidOperationException("boom"));
        var iterations = Recorder();

        using MeterListener listener = Listen(iterations);

        await RunAsync(useCase, enabled: true, () => useCase.CallCount >= 1, "one failing pass");

        iterations.ShouldContain(m => m.Outcome == "exception" && m.Value == 1);
    }

    private static ConcurrentQueue<(long Value, string? Outcome)> Recorder() => new();

    private static MeterListener Listen(ConcurrentQueue<(long Value, string? Outcome)> iterations)
    {
        var listener = new MeterListener
        {
            InstrumentPublished = (instrument, l) =>
            {
                if (instrument.Meter.Name == ReminderInstruments.Name)
                {
                    l.EnableMeasurementEvents(instrument);
                }
            },
        };

        listener.SetMeasurementEventCallback<long>((instrument, measurement, tags, _) =>
        {
            if (instrument.Name != "apptemplate.jobs.reminders.iterations")
            {
                return;
            }

            iterations.Enqueue((
                measurement,
                tags.ToArray().FirstOrDefault(t => t.Key == "outcome").Value as string));
        });

        listener.Start();

        return listener;
    }

    private static async Task RunAsync(
        FakeFireDueRemindersUseCase useCase,
        bool enabled,
        Func<bool> until,
        string description)
    {
        var services = new ServiceCollection();
        services.AddBackgroundScope();
        services.AddScoped<IFireDueRemindersUseCase>(_ => useCase);
        using var provider = services.BuildServiceProvider();

        var options = new JobsRemindersOptions { Interval = _tinyInterval, Enabled = enabled };

        using var service = new ReminderBackgroundService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(options),
            NullLogger<ReminderBackgroundService>.Instance);

        await service.StartAsync(TestContext.Current.CancellationToken);
        await BackgroundServiceProbe.WaitUntilAsync(until, description);
        await service.StopAsync(TestContext.Current.CancellationToken);
    }
}

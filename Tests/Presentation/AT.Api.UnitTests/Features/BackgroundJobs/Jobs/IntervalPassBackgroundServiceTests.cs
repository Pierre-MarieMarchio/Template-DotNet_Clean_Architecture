using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using AppTemplate.Api.Common.Jobs;
using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Presentation.Core.Common.Jobs;
using Mediator;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using Xunit;

namespace AppTemplate.Api.UnitTests.Features.BackgroundJobs.Jobs;

public sealed class IntervalPassBackgroundServiceTests : IDisposable
{
    private const string _label = "probe rows";

    private static readonly TimeSpan _interval = TimeSpan.FromMinutes(10);

    private readonly ActivitySource _activitySource = new($"{nameof(IntervalPassBackgroundServiceTests)}.{Guid.NewGuid():N}");
    private readonly Meter _meter = new($"{nameof(IntervalPassBackgroundServiceTests)}.{Guid.NewGuid():N}");
    private readonly ConcurrentQueue<(string Instrument, long Value, string? Task, string? Outcome)> _measurements = new();
    private readonly MeterListener _listener = new();
    private readonly FakeTimeProvider _clock = new();
    private readonly ConcurrentRecordingLogger<IntervalPassBackgroundService<ProbePassCommand>> _logger = new();
    private readonly JobPass _pass;

    public IntervalPassBackgroundServiceTests()
    {
        _pass = new JobPass(
            _label,
            "Probe rows will accumulate.",
            _activitySource,
            "probe.pass",
            _meter.CreateCounter<long>("probe.iterations"),
            _meter.CreateCounter<long>("probe.volume"));

        _listener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter == _meter)
            {
                listener.EnableMeasurementEvents(instrument);
            }
        };
        _listener.SetMeasurementEventCallback<long>((instrument, measurement, tags, _) =>
        {
            var tagArray = tags.ToArray();
            _measurements.Enqueue((
                instrument.Name,
                measurement,
                tagArray.FirstOrDefault(tag => tag.Key == "task").Value as string,
                tagArray.FirstOrDefault(tag => tag.Key == "outcome").Value as string));
        });
        _listener.Start();
    }

    [Fact]
    public async Task SendsTheCommandAtOnce_ThenOncePerInterval_OnTheClockItIsGiven()
    {
        var handler = new ProbePassHandler(() => Result.Success(4));

        using var service = Create(handler, enabled: true);

        await service.StartAsync(TestContext.Current.CancellationToken);
        await BackgroundServiceProbe.WaitUntilAsync(() => handler.CallCount == 1, "the first pass to run at start-up");

        _clock.Advance(_interval - TimeSpan.FromSeconds(1));
        handler.CallCount.ShouldBe(1, "a pass before its interval has elapsed runs too early");

        _clock.Advance(TimeSpan.FromSeconds(1));
        await BackgroundServiceProbe.WaitUntilAsync(() => handler.CallCount == 2, "the second pass to run once the interval elapsed");

        await service.StopAsync(TestContext.Current.CancellationToken);

        _measurements.ShouldContain(m => m.Instrument == "probe.iterations" && m.Task == _label && m.Outcome == "success");
        _measurements.ShouldContain(m => m.Instrument == "probe.volume" && m.Task == _label && m.Value == 4);
    }

    [Fact]
    public async Task APassThatFindsNothing_IsStillCounted()
    {
        var handler = new ProbePassHandler(() => Result.Success(0));

        using var service = Create(handler, enabled: true);

        await service.StartAsync(TestContext.Current.CancellationToken);
        await BackgroundServiceProbe.WaitUntilAsync(() => handler.CallCount == 1, "one pass");
        await service.StopAsync(TestContext.Current.CancellationToken);

        _measurements.ShouldContain(m => m.Instrument == "probe.iterations" && m.Outcome == "success" && m.Value == 1);
        _measurements.ShouldContain(m => m.Instrument == "probe.volume" && m.Value == 0);
    }

    [Fact]
    public async Task APassThatThrows_IsLoggedAndCounted_AndTheLoopRunsItAgainAtTheNextInterval()
    {
        var handler = new ProbePassHandler(() => throw new InvalidOperationException("boom"));

        using var service = Create(handler, enabled: true);

        await service.StartAsync(TestContext.Current.CancellationToken);
        await BackgroundServiceProbe.WaitUntilAsync(() => handler.CallCount == 1, "the first pass to have thrown");

        _clock.Advance(_interval);
        await BackgroundServiceProbe.WaitUntilAsync(() => handler.CallCount == 2, "the loop to survive the exception");

        await service.StopAsync(TestContext.Current.CancellationToken);

        _logger.Lines.ShouldContain(line => line.Level == LogLevel.Error && line.Message.Contains(_label, StringComparison.Ordinal));
        _measurements.ShouldContain(m => m.Instrument == "probe.iterations" && m.Task == _label && m.Outcome == "exception");
    }

    [Fact]
    public async Task APassThatFails_IsLoggedAsAWarningWithItsCode_AndCountedAsAFailure()
    {
        var handler = new ProbePassHandler(() => Result.Failure<int>(Error.Conflict("probe.busy", "The probe is busy.")));

        using var service = Create(handler, enabled: true);

        await service.StartAsync(TestContext.Current.CancellationToken);
        await BackgroundServiceProbe.WaitUntilAsync(() => handler.CallCount == 1, "one failing pass");
        await service.StopAsync(TestContext.Current.CancellationToken);

        _logger.Lines.ShouldContain(line => line.Level == LogLevel.Warning && line.Message.Contains("probe.busy", StringComparison.Ordinal));
        _measurements.ShouldContain(m => m.Instrument == "probe.iterations" && m.Task == _label && m.Outcome == "failure");
        _measurements.ShouldNotContain(m => m.Instrument == "probe.volume");
    }

    [Fact]
    public async Task ADisabledPass_SendsNothing_AndWarnsWhatItLeavesUndone_OnEveryTick()
    {
        var handler = new ProbePassHandler(() => Result.Success(1));

        using var service = Create(handler, enabled: false);

        await service.StartAsync(TestContext.Current.CancellationToken);
        await BackgroundServiceProbe.WaitUntilAsync(() => DisabledTicks() == 1, "the first tick to be counted as disabled");

        _clock.Advance(_interval);
        await BackgroundServiceProbe.WaitUntilAsync(() => DisabledTicks() == 2, "the second tick to be counted as disabled");

        await service.StopAsync(TestContext.Current.CancellationToken);

        handler.CallCount.ShouldBe(0);
        _logger.Lines.Count(line => line.Level == LogLevel.Warning && line.Message.Contains("Probe rows will accumulate.", StringComparison.Ordinal))
            .ShouldBe(2);
    }

    [Fact]
    public async Task StopAsync_ReturnsPromptly_AndLogsTheStop_WhenItLandsMidPass()
    {
        var handler = new ProbePassHandler(hangs: true);

        using var service = Create(handler, enabled: true);

        await service.StartAsync(CancellationToken.None);
        await BackgroundServiceProbe.WaitUntilAsync(() => handler.CallCount == 1, "the pass to be in flight");

        var stopTask = service.StopAsync(CancellationToken.None);
        var completed = await Task.WhenAny(stopTask, Task.Delay(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));

        completed.ShouldBe(stopTask, "StopAsync must cancel the pass in flight rather than wait for it");
        _logger.Lines.ShouldContain(line => line.Message.Contains($"The pass over {_label} is stopping", StringComparison.Ordinal));
        _logger.Lines.ShouldNotContain(line => line.Level == LogLevel.Error, "a cancelled pass is a stop, not a failure");
    }

    public void Dispose()
    {
        _listener.Dispose();
        _meter.Dispose();
        _activitySource.Dispose();
    }

    private int DisabledTicks() =>
        _measurements.Count(m => m.Instrument == "probe.iterations" && m.Task == _label && m.Outcome == "disabled");

    private IntervalPassBackgroundService<ProbePassCommand> Create(ProbePassHandler handler, bool enabled)
    {
        var services = new ServiceCollection();
        services.AddBackgroundScope();
        services.AddScoped<ISender, RoutingSender>();
        services.AddScoped<ICommandHandler<ProbePassCommand, Result<int>>>(_ => handler);
        var provider = services.BuildServiceProvider();

        return new IntervalPassBackgroundService<ProbePassCommand>(
            new IntervalPass(_pass, _interval, enabled),
            provider.GetRequiredService<IServiceScopeFactory>(),
            _logger,
            _clock);
    }

    internal sealed record ProbePassCommand : ICommand<Result<int>>;

    private sealed class ProbePassHandler(Func<Result<int>>? outcome = null, bool hangs = false)
        : ICommandHandler<ProbePassCommand, Result<int>>
    {
        private int _callCount;

        public int CallCount => Volatile.Read(ref _callCount);

        public async ValueTask<Result<int>> Handle(ProbePassCommand command, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _callCount);

            if (hangs)
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }

            return outcome!();
        }
    }
}

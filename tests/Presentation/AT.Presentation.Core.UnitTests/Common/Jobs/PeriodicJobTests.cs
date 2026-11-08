using AppTemplate.Presentation.Core.Common.Jobs;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using Xunit;

namespace AppTemplate.Presentation.Core.UnitTests.Common.Jobs;

public sealed class PeriodicJobTests
{
    private static readonly TimeSpan _tinyInterval = TimeSpan.FromMilliseconds(20);

    [Fact]
    public async Task RunAsync_RunsTheFirstIteration_WithoutWaitingOutTheInterval()
    {
        using var cancellation = new CancellationTokenSource();
        int iterations = 0;

        var job = PeriodicJob.RunAsync(
            TimeSpan.FromMinutes(10),
            TimeProvider.System,
            _ =>
            {
                iterations++;
                cancellation.Cancel();
                return Task.CompletedTask;
            },
            cancellation.Token);

        await job;

        iterations.ShouldBe(1);
    }

    [Fact]
    public async Task RunAsync_WaitsOutTheInterval_OnTheClockItIsGiven()
    {
        using var cancellation = new CancellationTokenSource();
        var clock = new FakeTimeProvider();
        int iterations = 0;

        var job = PeriodicJob.RunAsync(
            TimeSpan.FromMinutes(10),
            clock,
            _ =>
            {
                iterations++;
                return Task.CompletedTask;
            },
            cancellation.Token);

        await WaitUntilAsync(() => Volatile.Read(ref iterations) == 1);

        clock.Advance(TimeSpan.FromMinutes(9));
        Volatile.Read(ref iterations).ShouldBe(1);

        clock.Advance(TimeSpan.FromMinutes(1));
        await WaitUntilAsync(() => Volatile.Read(ref iterations) == 2);

        await cancellation.CancelAsync();
        await job;
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        while (!condition())
        {
            await Task.Delay(TimeSpan.FromMilliseconds(5), timeout.Token);
        }
    }

    [Fact]
    public async Task RunAsync_KeepsIterating_UntilTheTokenIsCancelled()
    {
        using var cancellation = new CancellationTokenSource();
        int iterations = 0;

        await PeriodicJob.RunAsync(
            _tinyInterval,
            TimeProvider.System,
            _ =>
            {
                if (++iterations >= 3)
                {
                    cancellation.Cancel();
                }

                return Task.CompletedTask;
            },
            cancellation.Token);

        iterations.ShouldBe(3);
    }

    [Fact]
    public async Task RunAsync_ReturnsNormally_WhenAnIterationIsCancelledMidFlight()
    {
        using var cancellation = new CancellationTokenSource();
        int iterations = 0;

        await PeriodicJob.RunAsync(
            _tinyInterval,
            TimeProvider.System,
            async token =>
            {
                iterations++;
                await cancellation.CancelAsync();
                token.ThrowIfCancellationRequested();
            },
            cancellation.Token);

        iterations.ShouldBe(1);
    }

    [Fact]
    public async Task RunAsync_ReturnsNormally_WhenTheTokenIsCancelledWhileWaiting()
    {
        using var cancellation = new CancellationTokenSource(_tinyInterval);

        await Should.NotThrowAsync(
            () => PeriodicJob.RunAsync(
                TimeSpan.FromMinutes(10),
                TimeProvider.System,
                _ => Task.CompletedTask,
                cancellation.Token));
    }

    [Fact]
    public async Task RunAsync_PropagatesAnyOtherFailure()
    {
        using var cancellation = new CancellationTokenSource();

        var exception = await Should.ThrowAsync<InvalidOperationException>(
            () => PeriodicJob.RunAsync(
                _tinyInterval,
                TimeProvider.System,
                _ => throw new InvalidOperationException("boom"),
                cancellation.Token));

        exception.Message.ShouldBe("boom");
    }
}

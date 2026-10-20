using AppTemplate.Presentation.Core.Common.Jobs;
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
    public async Task RunAsync_KeepsIterating_UntilTheTokenIsCancelled()
    {
        using var cancellation = new CancellationTokenSource();
        int iterations = 0;

        await PeriodicJob.RunAsync(
            _tinyInterval,
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
                _ => throw new InvalidOperationException("boom"),
                cancellation.Token));

        exception.Message.ShouldBe("boom");
    }

    [Fact]
    public async Task RunAsync_RefusesANullIteration() =>
        await Should.ThrowAsync<ArgumentNullException>(
            () => PeriodicJob.RunAsync(_tinyInterval, null!, CancellationToken.None));
}

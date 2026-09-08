using AppTemplate.Presentation.Core.Common.Jobs;
using Shouldly;
using Xunit;

namespace AppTemplate.Presentation.Core.UnitTests.Common.Jobs;

/// <summary>
/// The loop every recurring host job is built from: it runs at once rather than after a first
/// interval, it keeps running, and a cancellation ends it by returning instead of by throwing —
/// which is what lets a caller log its own shutdown on the line after the call.
/// </summary>
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

    /// <summary>
    /// The stop landing while an iteration is still running is the case a caller's shutdown line
    /// depends on: the iteration's own cancellation must not unwind through the loop.
    /// </summary>
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

        // Reaching this line at all is the property: the iteration's own cancellation ended the
        // loop instead of unwinding through it.
        iterations.ShouldBe(1);
    }

    [Fact]
    public async Task RunAsync_ReturnsNormally_WhenTheTokenIsCancelledWhileWaiting()
    {
        using var cancellation = new CancellationTokenSource(_tinyInterval);

        await PeriodicJob.RunAsync(
            TimeSpan.FromMinutes(10),
            _ => Task.CompletedTask,
            cancellation.Token);
    }

    /// <summary>
    /// Anything other than the shutdown ends the loop and reaches the caller, so a job that has to
    /// survive its own failures says so by handling them.
    /// </summary>
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

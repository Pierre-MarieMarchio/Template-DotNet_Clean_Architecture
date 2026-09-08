namespace AppTemplate.Presentation.Core.Common.Jobs;

/// <summary>Runs one asynchronous iteration on a fixed interval until the host stops.</summary>
public static class PeriodicJob
{
    /// <summary>
    /// Runs <paramref name="iteration"/> once immediately, then once per
    /// <paramref name="interval"/>, and returns when <paramref name="stoppingToken"/> is cancelled.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Ticks that elapse while an iteration is still running are coalesced into one, so an iteration
    /// never overlaps itself however long it takes. Two hosts running the same job are not
    /// coordinated by this: exclusivity between processes belongs to the operation.
    /// </para>
    /// <para>
    /// Cancellation ends the loop and returns normally, whether it arrives while waiting for the
    /// next tick or from inside an iteration, so whatever a caller does after this call always runs.
    /// Any other exception leaving <paramref name="iteration"/> propagates and ends the loop, so an
    /// iteration that must survive its own failures handles them itself.
    /// </para>
    /// </remarks>
    /// <param name="interval">Time between the start of one iteration and the next tick.</param>
    /// <param name="iteration">The work of one pass.</param>
    /// <param name="stoppingToken">Cancelled when the host is shutting down.</param>
    public static async Task RunAsync(
        TimeSpan interval,
        Func<CancellationToken, Task> iteration,
        CancellationToken stoppingToken)
    {
        ArgumentNullException.ThrowIfNull(iteration);

        using var timer = new PeriodicTimer(interval);

        try
        {
            do
            {
                await iteration(stoppingToken);
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutdown is how this loop ends, not a failure to report.
        }
    }
}

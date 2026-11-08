namespace AppTemplate.Presentation.Core.Common.Jobs;

public static class PeriodicJob
{
    public static async Task RunAsync(
        TimeSpan interval,
        TimeProvider timeProvider,
        Func<CancellationToken, Task> iteration,
        CancellationToken stoppingToken)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(iteration);

        using var timer = new PeriodicTimer(interval, timeProvider);

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
        }
    }
}

namespace AppTemplate.EndToEnd.Tests;

internal static class Poll
{
    internal static async Task<T> UntilAsync<T>(
        string awaited,
        TimeSpan within,
        Func<CancellationToken, Task<T?>> attempt,
        CancellationToken cancellationToken)
        where T : class
    {
        var deadline = DateTimeOffset.UtcNow + within;

        while (true)
        {
            if (await attempt(cancellationToken) is { } result)
            {
                return result;
            }

            if (DateTimeOffset.UtcNow > deadline)
            {
                throw new TimeoutException($"{awaited} did not happen within {within}.");
            }

            await Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken);
        }
    }
}

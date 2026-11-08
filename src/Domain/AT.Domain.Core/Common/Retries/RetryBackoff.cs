namespace AppTemplate.Domain.Core.Common.Retries;

public static class RetryBackoff
{
    private static readonly TimeSpan _firstDelay = TimeSpan.FromMinutes(1);

    private static readonly TimeSpan _longestDelay = TimeSpan.FromHours(1);

    public static TimeSpan After(int attempts)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(attempts, 1);

        var delay = _firstDelay;

        for (int attempt = 1; attempt < attempts && delay < _longestDelay; attempt++)
        {
            delay += delay;
        }

        return delay < _longestDelay ? delay : _longestDelay;
    }
}

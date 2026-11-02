using Microsoft.EntityFrameworkCore;

namespace AppTemplate.Infrastructure.Auth.IntegrationTests.Fixtures;

internal static class DatabaseReadiness
{
    private static readonly TimeSpan _ceiling = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan _pollInterval = TimeSpan.FromMilliseconds(200);

    internal static async Task WaitAsync(DbContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        using var timeout = new CancellationTokenSource(_ceiling);
        Exception? last = null;

        while (!timeout.IsCancellationRequested)
        {
            try
            {
                if (await context.Database.CanConnectAsync(cancellationToken))
                {
                    return;
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                last = exception;
            }

            await Task.Delay(_pollInterval, cancellationToken);
        }

        throw new InvalidOperationException(
            $"The container's database did not accept a connection within {_ceiling.TotalSeconds:N0}s.",
            last);
    }
}

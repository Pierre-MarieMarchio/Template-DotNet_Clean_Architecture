using AppTemplate.Infrastructure.Persistence.Common.Contexts;

namespace AppTemplate.Infrastructure.Identity.IntegrationTests.Fixtures;

/// <summary>
/// Waits until a started container's database actually accepts a connection.
/// </summary>
/// <remarks>
/// <para>
/// <b>A precondition on the container, not a retry over an assertion.</b> Four containers start at
/// once across this solution, and the official PostgreSQL image restarts itself once while
/// initialising, so a port that answered a readiness check can refuse the next connection:
/// <c>MigrateAsync</c> then fails with <c>Connection refused</c> even though <c>StartAsync</c> has
/// returned. That is the cause the evidence points at rather than one observed being fixed, which is
/// worth knowing before anyone deletes the wait as ceremony.
/// </para>
/// <para>
/// Nothing this waits on is product code, which is what makes it a wait rather than a mask: the
/// failure it removes is a fixture asking a database a question before there is a database to ask.
/// A real refusal — wrong credentials, a container that dies — still fails, at the ceiling, naming
/// the last error rather than swallowing it.
/// </para>
/// </remarks>
internal static class DatabaseReadiness
{
    private static readonly TimeSpan _ceiling = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan _pollInterval = TimeSpan.FromMilliseconds(200);

    internal static async Task WaitAsync(AppDbContext context, CancellationToken cancellationToken)
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
                // Kept rather than logged: if the ceiling is reached this is the only description of
                // why, and a swallowed connection error is the thing this class must not become.
                last = exception;
            }

            await Task.Delay(_pollInterval, cancellationToken);
        }

        throw new InvalidOperationException(
            $"The container's database did not accept a connection within {_ceiling.TotalSeconds:N0}s.",
            last);
    }
}

using System.Collections.Concurrent;
using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Infrastructure.Core.Common.Contexts;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace AppTemplate.Infrastructure.Persistence.Common.Leases;

internal sealed class LeaderLease(IConfiguration configuration, ILogger<LeaderLease> logger)
    : ILeaderLease
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> _leases = new(StringComparer.Ordinal);

    private readonly string _database = Path.GetFullPath(
        new SqliteConnectionStringBuilder(DefaultConnectionString.Require(configuration)).DataSource);

    public async Task<bool> TryRunExclusivelyAsync(
        string leaseName,
        Func<CancellationToken, Task> work,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(leaseName);
        ArgumentNullException.ThrowIfNull(work);

        var lease = _leases.GetOrAdd($"{_database}|{leaseName}", _ => new SemaphoreSlim(1, 1));

        if (!await lease.WaitAsync(TimeSpan.Zero, cancellationToken))
        {
            if (logger.IsEnabled(LogLevel.Debug))
            {
                logger.LogDebug("The '{LeaseName}' lease is held by another runner of this process.", leaseName);
            }

            return false;
        }

        try
        {
            await work(cancellationToken);
        }
        finally
        {
            lease.Release();
        }

        return true;
    }
}

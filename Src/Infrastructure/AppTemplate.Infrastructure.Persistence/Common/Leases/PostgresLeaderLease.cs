using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Infrastructure.Core.Common.Contexts;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace AppTemplate.Infrastructure.Persistence.Common.Leases;

internal sealed class PostgresLeaderLease(IConfiguration configuration, ILogger<PostgresLeaderLease> logger)
    : ILeaderLease
{
    private const string _acquireSql = "SELECT pg_try_advisory_lock(@key)";

    private const string _releaseSql = "SELECT pg_advisory_unlock(@key)";

    private readonly string _connectionString =
        LeaseConnectionString(DefaultConnectionString.Require(configuration));

    public async Task<bool> TryRunExclusivelyAsync(
        string leaseName,
        Func<CancellationToken, Task> work,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(leaseName);
        ArgumentNullException.ThrowIfNull(work);

        long key = AdvisoryLockKey.Of(leaseName);

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        if (!await TryAcquireAsync(connection, key, cancellationToken))
        {
            return false;
        }

        try
        {
            await work(cancellationToken);
        }
        finally
        {
            await ReleaseAsync(connection, key, leaseName);
        }

        return true;
    }

    private static async Task<bool> TryAcquireAsync(
        NpgsqlConnection connection,
        long key,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(_acquireSql, connection);
        command.Parameters.AddWithValue("key", key);

        return await command.ExecuteScalarAsync(cancellationToken) is true;
    }

    private async Task ReleaseAsync(NpgsqlConnection connection, long key, string leaseName)
    {
        try
        {
            await using var command = new NpgsqlCommand(_releaseSql, connection);
            command.Parameters.AddWithValue("key", key);

            if (await command.ExecuteScalarAsync(CancellationToken.None) is not true)
            {
                logger.LogWarning(
                    "The '{LeaseName}' lease was not held by this session at release time.",
                    leaseName);
            }
        }
        catch (NpgsqlException exception)
        {
            logger.LogWarning(
                exception,
                "Releasing the '{LeaseName}' lease failed; the lock goes when the session closes.",
                leaseName);
        }
    }

    private static string LeaseConnectionString(string connectionString) =>
        new NpgsqlConnectionStringBuilder(connectionString) { Pooling = false }.ConnectionString;
}

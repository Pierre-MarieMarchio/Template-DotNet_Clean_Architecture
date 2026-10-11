using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Infrastructure.Core.Common.Contexts;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace AppTemplate.Infrastructure.Persistence.Common.Leases;

/// <summary>Leadership by PostgreSQL advisory lock: <c>pg_try_advisory_lock</c> succeeds for
/// exactly one session and returns <c>false</c> to every other without waiting, so one host runs the
/// work and the rest are standbys. The decisive property is that the lock belongs to the *session* —
/// losing the process releases it, rather than leaving a lease to expire on a timer nobody holds.</summary>
/// <remarks>Named for the store the lock is taken from, so a lease held anywhere else can satisfy the
/// same port beside it. Holds no state between calls and must be a singleton: the background services
/// using it are singletons, and a scoped dependency captured by one is what the container's scope
/// validation refuses.</remarks>
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

        // An advisory lock is held by the session, not by the command or transaction that took it,
        // so the lease owns its connection: a borrowed one would release the lock when EF chose to.
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

    /// <summary>Releases the lock, best effort, from a <c>finally</c>.</summary>
    /// <remarks>Deliberately not given the caller's token: the case that most needs releasing is
    /// the cancelled one, and a cancelled token would make this throw before doing anything. Closing
    /// the session releases the lock in any case, so a failure here is a log line, not an exception —
    /// this call makes the lock go away *promptly*, it is not what makes it go away.</remarks>
    private async Task ReleaseAsync(NpgsqlConnection connection, long key, string leaseName)
    {
        try
        {
            await using var command = new NpgsqlCommand(_releaseSql, connection);
            command.Parameters.AddWithValue("key", key);

            if (await command.ExecuteScalarAsync(CancellationToken.None) is not true)
            {
                // The session did not hold the lock it just released: either the connection was
                // re-established mid-lease, or two calls are sharing a session.
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

    /// <summary>The same database, on a connection that is not pooled.</summary>
    /// <remarks>A lease connection is held for the whole of the work it guards — minutes, where a
    /// pool exists to amortise milliseconds — so pooling it would keep a pool slot out of use for
    /// exactly as long, and would put the session's lifetime in the pool's hands. The session's
    /// lifetime is the guarantee: the lock must disappear when this process does, not when a pooled
    /// connection is next recycled.</remarks>
    private static string LeaseConnectionString(string connectionString) =>
        new NpgsqlConnectionStringBuilder(connectionString) { Pooling = false }.ConnectionString;
}

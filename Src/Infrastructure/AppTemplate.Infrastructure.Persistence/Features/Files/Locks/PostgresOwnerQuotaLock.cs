using System.Globalization;
using AppTemplate.Application.Features.Files.Ports.OwnerQuotaLock;
using AppTemplate.Domain.Core.Common.Primitives;
using AppTemplate.Domain.Features.Files.Model.ValueObjects;
using AppTemplate.Infrastructure.Persistence.Common.Leases;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace AppTemplate.Infrastructure.Persistence.Features.Files.Locks;

/// <summary>
/// The owner's quota lock as a PostgreSQL session advisory lock, so every host sharing the database
/// contends for the same key. Taken with <c>pg_advisory_lock</c> on a dedicated connection and
/// released with <c>pg_advisory_unlock</c> once the work, commit included, has returned.
/// </summary>
/// <remarks>
/// The connection is not pooled: the lock belongs to the session, so it must end with this call
/// rather than when a pool recycles the connection, and must not hold a pooled connection the
/// registration itself needs. Stateless; registered as a singleton.
/// </remarks>
internal sealed class PostgresOwnerQuotaLock(string connectionString, ILogger<PostgresOwnerQuotaLock> logger)
    : IOwnerQuotaLock
{
    /// <summary>How long a registration waits for another one of the same owner and category.</summary>
    internal static readonly TimeSpan WaitLimit = TimeSpan.FromSeconds(10);

    // Free of the project name on purpose: the template engine renames that name wherever it
    // appears, which would change the key and break the digest the unit test pins.
    private const string _keyNamespace = "files.quota";

    private const string _setTimeoutSql = "SELECT set_config('lock_timeout', @timeout, false)";

    private const string _acquireSql = "SELECT pg_advisory_lock(@key)";

    private const string _releaseSql = "SELECT pg_advisory_unlock(@key)";

    private readonly string _connectionString =
        new NpgsqlConnectionStringBuilder(connectionString) { Pooling = false }.ConnectionString;

    public async Task<T> RunExclusivelyAsync<T>(
        UserId owner,
        FileCategory category,
        Func<CancellationToken, Task<T>> work,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(category);
        ArgumentNullException.ThrowIfNull(work);

        long key = KeyOf(owner, category);

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await AcquireAsync(connection, key, cancellationToken);

        try
        {
            return await work(cancellationToken);
        }
        finally
        {
            await ReleaseAsync(connection, key);
        }
    }

    internal static long KeyOf(UserId owner, FileCategory category) =>
        AdvisoryLockKey.Of(string.Create(
            CultureInfo.InvariantCulture,
            $"{_keyNamespace}:{owner.Value:D}:{category.Value}"));

    private static async Task AcquireAsync(NpgsqlConnection connection, long key, CancellationToken cancellationToken)
    {
        await using (var setTimeout = new NpgsqlCommand(_setTimeoutSql, connection))
        {
            setTimeout.Parameters.AddWithValue(
                "timeout",
                string.Create(CultureInfo.InvariantCulture, $"{(long)WaitLimit.TotalMilliseconds}ms"));
            await setTimeout.ExecuteScalarAsync(cancellationToken);
        }

        try
        {
            await using var acquire = new NpgsqlCommand(_acquireSql, connection);
            acquire.Parameters.AddWithValue("key", key);
            await acquire.ExecuteScalarAsync(cancellationToken);
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.LockNotAvailable)
        {
            throw new OwnerQuotaLockTimeoutException(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"The owner's quota lock was not obtained within {WaitLimit.TotalSeconds:0} seconds."),
                exception);
        }
    }

    /// <summary>
    /// Best effort and without the caller's token: a cancelled token would skip the release, and
    /// closing the session releases the lock anyway.
    /// </summary>
    private async Task ReleaseAsync(NpgsqlConnection connection, long key)
    {
        try
        {
            await using var command = new NpgsqlCommand(_releaseSql, connection);
            command.Parameters.AddWithValue("key", key);
            await command.ExecuteScalarAsync(CancellationToken.None);
        }
        catch (NpgsqlException exception)
        {
            logger.LogWarning(
                exception,
                "Releasing an owner's quota lock failed; it goes when the session closes.");
        }
    }
}

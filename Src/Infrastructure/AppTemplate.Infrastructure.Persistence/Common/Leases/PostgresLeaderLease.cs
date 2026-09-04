using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using AppTemplate.Application.Common.Abstractions;
using AppTemplate.Infrastructure.Persistence.Common.Contexts;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace AppTemplate.Infrastructure.Persistence.Common.Leases;

/// <summary>
/// Leadership by PostgreSQL advisory lock. <c>pg_try_advisory_lock</c> succeeds for exactly one
/// session and returns <c>false</c> to every other one without waiting, so one host runs the work
/// and the rest are standbys. The decisive property is that the lock belongs to the *session*:
/// losing the process closes the session and releases the lock, rather than leaving a lease to
/// expire on a timer nobody is holding.
/// </summary>
/// <remarks>
/// Named for the module and not simply <c>LeaderLease</c>, which is what this repository's rule
/// would otherwise give: leadership here is a property of the store it is taken from, and an
/// in-memory lease for a test host or another module's would satisfy the same port beside it — the
/// same reason <c>MailKitEmailSender</c> and <c>InMemoryEmailSender</c> both carry a prefix.
/// <para>
/// Holds no state between calls and must be registered as a singleton: the background services that
/// use it are singletons, and a scoped dependency captured by one is exactly what the container's
/// scope validation refuses.
/// </para>
/// </remarks>
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

        long key = KeyOf(leaseName);

        // Its own connection, opened here and closed when this call ends. An advisory lock is held
        // by the session rather than by the command or the transaction that took it, so borrowing
        // the DbContext's connection would hand the lock's lifetime to EF: the connection goes back
        // to the pool still inside a lease this call believes it owns, and is disposed — releasing
        // the lock — at a moment this call did not choose. Neither end of that lifetime is ours
        // unless the connection is.
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

    /// <summary>
    /// Releases the lock, best effort, from a <c>finally</c>.
    /// </summary>
    /// <remarks>
    /// Deliberately not given the caller's token. The case that most needs releasing is the
    /// cancelled one, and a cancelled token would make this throw before it did anything — replacing
    /// the exception the caller is already being handed with one about the cleanup. Closing the
    /// session releases the lock in any case, which is why a failure here is a log line and not an
    /// exception: this call is what makes the lock go away *promptly*, not what makes it go away.
    /// </remarks>
    private async Task ReleaseAsync(NpgsqlConnection connection, long key, string leaseName)
    {
        try
        {
            await using var command = new NpgsqlCommand(_releaseSql, connection);
            command.Parameters.AddWithValue("key", key);

            if (await command.ExecuteScalarAsync(CancellationToken.None) is not true)
            {
                // The session did not hold the lock it just released. Either the connection was
                // re-established under us — in which case the work ran without the exclusion it
                // asked for — or two calls are sharing a session they should not be.
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

    /// <summary>
    /// The <c>bigint</c> the lock is taken on, derived from the lease name.
    /// </summary>
    /// <remarks>
    /// SHA-256 truncated to eight bytes, read big-endian — chosen against the two shortcuts that
    /// look equivalent and are not. <c>string.GetHashCode()</c> is randomised per process, and
    /// <c>BitConverter.ToInt64</c> follows the machine's endianness; either one gives two replicas
    /// two different keys. Each would then take a lock nothing else contends for, every host would
    /// believe it is the leader, and the mechanism would serialise nothing while looking perfectly
    /// healthy — a single-process test cannot see the difference. This mapping has to hold across
    /// processes, machines and releases, so it is written out explicitly rather than delegated to
    /// anything whose stability is not part of its contract.
    /// </remarks>
    private static long KeyOf(string leaseName)
    {
        Span<byte> digest = stackalloc byte[SHA256.HashSizeInBytes];
        SHA256.HashData(Encoding.UTF8.GetBytes(leaseName), digest);

        return BinaryPrimitives.ReadInt64BigEndian(digest);
    }

    /// <summary>
    /// The same database, on a connection that is not pooled.
    /// </summary>
    /// <remarks>
    /// A lease connection is checked out for the whole of the work it guards — minutes, where a pool
    /// exists to amortise connections held for milliseconds — so pooling it would keep a slot of the
    /// shared pool out of use for exactly as long as the loop runs. It would also put the session's
    /// lifetime in the pool's hands, and the session's lifetime is the guarantee: the lock must
    /// disappear when this process does, not when a pooled connection is next recycled.
    /// </remarks>
    private static string LeaseConnectionString(string connectionString) =>
        new NpgsqlConnectionStringBuilder(connectionString) { Pooling = false }.ConnectionString;
}

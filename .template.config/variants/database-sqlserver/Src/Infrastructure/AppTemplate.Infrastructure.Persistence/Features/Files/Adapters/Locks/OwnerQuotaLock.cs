using System.Data;
using System.Globalization;
using AppTemplate.Application.Features.Files.Ports.OwnerQuotaLock;
using AppTemplate.Domain.Core.Common.Primitives;
using AppTemplate.Domain.Features.Files.Model.ValueObjects;
using AppTemplate.Infrastructure.Core.Common.Contexts;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace AppTemplate.Infrastructure.Persistence.Features.Files.Adapters.Locks;

internal sealed class OwnerQuotaLock(string connectionString, ILogger<OwnerQuotaLock> logger)
    : IOwnerQuotaLock
{
    internal static readonly TimeSpan WaitLimit = TimeSpan.FromSeconds(10);

    private const string _keyNamespace = "files.quota";

    private const int _resourceMaxLength = 255;

    private const int _timedOut = -1;

    private const string _acquireSql = """
        DECLARE @result int;
        EXEC @result = sp_getapplock
            @Resource = @resource,
            @LockMode = 'Exclusive',
            @LockOwner = 'Session',
            @LockTimeout = @timeout;
        SELECT @result;
        """;

    private const string _releaseSql = "EXEC sp_releaseapplock @Resource = @resource, @LockOwner = 'Session';";

    private readonly string _connectionString =
        DatabaseProvider.DedicatedConnectionString(connectionString);

    public async Task<T> RunExclusivelyAsync<T>(
        UserId owner,
        FileCategory category,
        Func<CancellationToken, Task<T>> work,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(category);
        ArgumentNullException.ThrowIfNull(work);

        string key = KeyOf(owner, category);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(key.Length, _resourceMaxLength);

        await using var connection = new SqlConnection(_connectionString);
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

    internal static string KeyOf(UserId owner, FileCategory category) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"{_keyNamespace}:{owner.Value:D}:{category.Value}");

    private static async Task AcquireAsync(SqlConnection connection, string key, CancellationToken cancellationToken)
    {
        await using var acquire = CommandFor(connection, _acquireSql, key);
        acquire.Parameters.Add("@timeout", SqlDbType.Int).Value = (int)WaitLimit.TotalMilliseconds;

        int result = (int)(await acquire.ExecuteScalarAsync(cancellationToken))!;

        if (result == _timedOut)
        {
            throw new OwnerQuotaLockTimeoutException(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"The owner's quota lock was not obtained within {WaitLimit.TotalSeconds:0} seconds."));
        }

        if (result < 0)
        {
            throw new InvalidOperationException(
                string.Create(CultureInfo.InvariantCulture, $"sp_getapplock refused the owner's quota lock with code {result}."));
        }
    }

    private async Task ReleaseAsync(SqlConnection connection, string key)
    {
        try
        {
            await using var command = CommandFor(connection, _releaseSql, key);
            await command.ExecuteNonQueryAsync(CancellationToken.None);
        }
        catch (SqlException exception)
        {
            logger.LogWarning(
                exception,
                "Releasing an owner's quota lock failed; it goes when the session closes.");
        }
    }

    private static SqlCommand CommandFor(SqlConnection connection, string sql, string key)
    {
        var command = new SqlCommand(sql, connection);
        command.Parameters.Add("@resource", SqlDbType.NVarChar, _resourceMaxLength).Value = key;

        return command;
    }
}

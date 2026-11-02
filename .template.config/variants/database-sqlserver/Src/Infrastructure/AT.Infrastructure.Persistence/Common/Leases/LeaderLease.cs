using System.Data;
using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Infrastructure.Core.Common.Contexts;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace AppTemplate.Infrastructure.Persistence.Common.Leases;

internal sealed class LeaderLease(IConfiguration configuration, ILogger<LeaderLease> logger)
    : ILeaderLease
{
    private const int _resourceMaxLength = 255;

    private const string _acquireSql = """
        DECLARE @result int;
        EXEC @result = sp_getapplock
            @Resource = @resource,
            @LockMode = 'Exclusive',
            @LockOwner = 'Session',
            @LockTimeout = 0;
        SELECT @result;
        """;

    private const string _releaseSql = """
        DECLARE @result int;
        EXEC @result = sp_releaseapplock @Resource = @resource, @LockOwner = 'Session';
        SELECT @result;
        """;

    private readonly string _connectionString =
        DatabaseProvider.DedicatedConnectionString(DefaultConnectionString.Require(configuration));

    public async Task<bool> TryRunExclusivelyAsync(
        string leaseName,
        Func<CancellationToken, Task> work,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(leaseName);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(leaseName.Length, _resourceMaxLength);
        ArgumentNullException.ThrowIfNull(work);

        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        if (!await TryAcquireAsync(connection, leaseName, cancellationToken))
        {
            return false;
        }

        try
        {
            await work(cancellationToken);
        }
        finally
        {
            await ReleaseAsync(connection, leaseName);
        }

        return true;
    }

    private static async Task<bool> TryAcquireAsync(
        SqlConnection connection,
        string leaseName,
        CancellationToken cancellationToken)
    {
        await using var command = CommandFor(connection, _acquireSql, leaseName);

        return await command.ExecuteScalarAsync(cancellationToken) is >= 0;
    }

    private async Task ReleaseAsync(SqlConnection connection, string leaseName)
    {
        try
        {
            await using var command = CommandFor(connection, _releaseSql, leaseName);

            if (await command.ExecuteScalarAsync(CancellationToken.None) is not 0)
            {
                logger.LogWarning(
                    "The '{LeaseName}' lease was not held by this session at release time.",
                    leaseName);
            }
        }
        catch (SqlException exception)
        {
            logger.LogWarning(
                exception,
                "Releasing the '{LeaseName}' lease failed; the lock goes when the session closes.",
                leaseName);
        }
    }

    private static SqlCommand CommandFor(SqlConnection connection, string sql, string leaseName)
    {
        var command = new SqlCommand(sql, connection);
        command.Parameters.Add("@resource", SqlDbType.NVarChar, _resourceMaxLength).Value = leaseName;

        return command;
    }
}

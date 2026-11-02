using System.Collections.Concurrent;
using System.Data.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace AppTemplate.Infrastructure.Core.Common.Contexts;

internal sealed class SqliteDatabaseFile : DbConnectionInterceptor
{
    private const string _writeAheadLogSql = "PRAGMA journal_mode=WAL";

    private static readonly ConcurrentDictionary<string, bool> _journaled = new(StringComparer.Ordinal);

    private SqliteDatabaseFile()
    {
    }

    internal static SqliteDatabaseFile Interceptor { get; } = new();

    public override InterceptionResult ConnectionOpening(
        DbConnection connection,
        ConnectionEventData eventData,
        InterceptionResult result)
    {
        CreateFolder(connection);

        return result;
    }

    public override ValueTask<InterceptionResult> ConnectionOpeningAsync(
        DbConnection connection,
        ConnectionEventData eventData,
        InterceptionResult result,
        CancellationToken cancellationToken = default)
    {
        CreateFolder(connection);

        return ValueTask.FromResult(result);
    }

    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        if (IsFirstOpening(connection))
        {
            using var command = connection.CreateCommand();
            command.CommandText = _writeAheadLogSql;
            command.ExecuteNonQuery();
        }
    }

    public override async Task ConnectionOpenedAsync(
        DbConnection connection,
        ConnectionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        if (IsFirstOpening(connection))
        {
            await using var command = connection.CreateCommand();
            command.CommandText = _writeAheadLogSql;
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static void CreateFolder(DbConnection connection)
    {
        if (FileOf(connection, SqliteOpenMode.ReadWriteCreate) is { } path
            && Path.GetDirectoryName(path) is { Length: > 0 } folder)
        {
            Directory.CreateDirectory(folder);
        }
    }

    private static bool IsFirstOpening(DbConnection connection) =>
        (FileOf(connection, SqliteOpenMode.ReadWriteCreate) ?? FileOf(connection, SqliteOpenMode.ReadWrite)) is { } path
        && _journaled.TryAdd(path, true);

    private static string? FileOf(DbConnection connection, SqliteOpenMode mode)
    {
        var builder = new SqliteConnectionStringBuilder(connection.ConnectionString);

        return builder.Mode == mode
            && !string.IsNullOrEmpty(builder.DataSource)
            && builder.DataSource != ":memory:"
            && !builder.DataSource.StartsWith("file:", StringComparison.OrdinalIgnoreCase)
                ? Path.GetFullPath(builder.DataSource)
                : null;
    }
}

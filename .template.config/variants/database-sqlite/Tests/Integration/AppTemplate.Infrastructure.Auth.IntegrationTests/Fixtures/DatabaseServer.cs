using System.Globalization;
using Microsoft.Data.Sqlite;

namespace AppTemplate.Infrastructure.Auth.IntegrationTests.Fixtures;

public sealed class DatabaseServer(string database) : IAsyncDisposable
{
    private const int _busy = 5;

    private readonly string _path = Path.Combine(
        Path.GetTempPath(),
        string.Create(CultureInfo.InvariantCulture, $"{database}-{Guid.CreateVersion7():N}.db"));

    public string ConnectionString => new SqliteConnectionStringBuilder { DataSource = _path }.ToString();

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var connection = new SqliteConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
    }

    public ValueTask DisposeAsync()
    {
        SqliteConnection.ClearAllPools();

        foreach (string file in new[] { _path, _path + "-wal", _path + "-shm" })
        {
            File.Delete(file);
        }

        return ValueTask.CompletedTask;
    }

    internal static string Table(string schema, string table) => $"\"{schema}_{table}\"";

    internal static Exception TransientFault() =>
        new SqliteException("Injected: the database is locked.", _busy);
}

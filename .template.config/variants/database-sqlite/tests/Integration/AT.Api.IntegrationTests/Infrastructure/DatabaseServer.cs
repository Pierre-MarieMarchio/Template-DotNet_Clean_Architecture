using System.Globalization;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace AppTemplate.Api.IntegrationTests.Infrastructure;

public sealed class DatabaseServer(string database) : IAsyncDisposable
{
    private static readonly DateTimeOffsetToBinaryConverter _instants = new();

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

    internal static object Value(Guid value) => value.ToString("D").ToUpperInvariant();

    internal static object Value(DateTimeOffset value) => _instants.ConvertToProvider(value)!;

    internal static string Literal(Guid value) => $"'{Value(value)}'";

    internal static string Literal(DateTimeOffset value) =>
        Convert.ToString(Value(value), CultureInfo.InvariantCulture)!;

    internal static string SchemasSql =>
        "SELECT DISTINCT substr(name, 1, instr(name, '_') - 1) FROM sqlite_master WHERE type = 'table' AND instr(name, '_') > 1";

    internal static string HistorySchemasSql(string historyTable) =>
        $"""
        SELECT substr(name, 1, length(name) - {historyTable.Length + 1})
        FROM sqlite_master
        WHERE type = 'table' AND substr(name, -{historyTable.Length + 1}) = '_{historyTable}'
        ORDER BY 1
        """;

    internal static string TablesSql(IEnumerable<string> schemas, IEnumerable<string> keptTables)
    {
        string[] prefixes = [.. schemas.Select(schema => schema + "_")];
        string[] kept = [.. prefixes.SelectMany(prefix => keptTables.Select(table => prefix + table))];

        return $"""
            SELECT '"' || name || '"'
            FROM sqlite_master
            WHERE type = 'table'
              AND ({string.Join(" OR ", prefixes.Select(prefix => $"substr(name, 1, {prefix.Length}) = '{prefix}'"))})
              AND name NOT IN ({string.Join(", ", kept.Select(table => $"'{table}'"))})
            ORDER BY name
            """;
    }

    internal static string ResetSql(IEnumerable<string> tables) =>
        $"""
        PRAGMA foreign_keys = OFF;
        {string.Join(Environment.NewLine, tables.Select(table => $"DELETE FROM {table};"))}
        PRAGMA foreign_keys = ON;
        """;
}

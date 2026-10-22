using System.Data;
using Microsoft.Data.SqlClient;
using Testcontainers.MsSql;

namespace AppTemplate.Api.IntegrationTests.Infrastructure;

public sealed class DatabaseServer(string database) : IAsyncDisposable
{
    internal const string Image =
        "mcr.microsoft.com/mssql/server:2022-CU27-ubuntu-22.04@sha256:4402d880dd4c34bfa7d8705e56a86cd6c88da80a1f6bbbe741f999e76264a090";

    private const string _createDatabaseSql =
        "DECLARE @sql nvarchar(max) = N'CREATE DATABASE ' + QUOTENAME(@database); EXEC (@sql);";

    // One server per test assembly, each instance getting its own database: a SQL Server container takes about 2 GB of memory.
    private static readonly SemaphoreSlim _startGate = new(1, 1);

    private static MsSqlContainer? _container;

    private readonly string _database = $"{database}_{Guid.NewGuid():N}";

    public string ConnectionString =>
        new SqlConnectionStringBuilder(Container.GetConnectionString()) { InitialCatalog = _database }.ConnectionString;

    private static MsSqlContainer Container =>
        _container ?? throw new InvalidOperationException($"{nameof(StartAsync)} has not been called.");

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await StartContainerAsync(cancellationToken);

        await using var connection = new SqlConnection(Container.GetConnectionString());
        await connection.OpenAsync(cancellationToken);

        await using var command = new SqlCommand(_createDatabaseSql, connection);
        command.Parameters.Add("@database", SqlDbType.NVarChar, 128).Value = _database;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    internal static string Table(string schema, string table) => $"[{schema}].[{table}]";

    internal static object Value(Guid value) => value;

    internal static object Value(DateTimeOffset value) => value;

    internal static string Literal(Guid value) => $"'{value}'";

    internal static string Literal(DateTimeOffset value) => $"'{value:O}'";

    internal static string SchemasSql =>
        "SELECT DISTINCT table_schema FROM information_schema.tables WHERE table_type = 'BASE TABLE'";

    // The business context names "public" as its history schema, which SQL Server keeps in dbo.
    internal static string HistorySchemasSql(string historyTable) =>
        $"SELECT CASE table_schema WHEN 'dbo' THEN 'public' ELSE table_schema END AS table_schema FROM information_schema.tables WHERE table_name = '{historyTable}' ORDER BY 1";

    internal static string TablesSql(IEnumerable<string> schemas, IEnumerable<string> keptTables) =>
        $"""
        SELECT QUOTENAME(table_schema) + N'.' + QUOTENAME(table_name)
        FROM information_schema.tables
        WHERE table_schema IN ({string.Join(", ", schemas.Select(schema => $"N'{schema}'"))})
          AND table_type = 'BASE TABLE'
          AND table_name NOT IN ({string.Join(", ", keptTables.Select(table => $"N'{table}'"))})
        ORDER BY table_schema, table_name
        """;

    internal static string ResetSql(IEnumerable<string> tables)
    {
        var all = tables.ToList();

        return string.Concat(
            all.Select(table => $"ALTER TABLE {table} NOCHECK CONSTRAINT ALL;\n")
                .Concat(all.Select(table => $"DELETE FROM {table};\n"))
                .Concat(all.Select(table => $"ALTER TABLE {table} WITH CHECK CHECK CONSTRAINT ALL;\n")));
    }

    private static async Task StartContainerAsync(CancellationToken cancellationToken)
    {
        await _startGate.WaitAsync(cancellationToken);

        try
        {
            if (_container is not null)
            {
                return;
            }

            var container = new MsSqlBuilder(Image)
                .WithPassword("Apptemplate_tests_1")
                .WithEnvironment("MSSQL_MEMORY_LIMIT_MB", "1024")
                .Build();

            await container.StartAsync(cancellationToken);
            AppDomain.CurrentDomain.ProcessExit += (_, _) => container.DisposeAsync().AsTask().GetAwaiter().GetResult();
            _container = container;
        }
        finally
        {
            _startGate.Release();
        }
    }
}

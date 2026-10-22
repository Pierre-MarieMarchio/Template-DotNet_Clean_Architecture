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

    private readonly MsSqlContainer _container = new MsSqlBuilder(Image)
        .WithPassword("Apptemplate_tests_1")
        .Build();

    public string ConnectionString =>
        new SqlConnectionStringBuilder(_container.GetConnectionString()) { InitialCatalog = database }.ConnectionString;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await _container.StartAsync(cancellationToken);

        await using var connection = new SqlConnection(_container.GetConnectionString());
        await connection.OpenAsync(cancellationToken);

        await using var command = new SqlCommand(_createDatabaseSql, connection);
        command.Parameters.Add("@database", SqlDbType.NVarChar, 128).Value = database;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public ValueTask DisposeAsync() => _container.DisposeAsync();

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
}

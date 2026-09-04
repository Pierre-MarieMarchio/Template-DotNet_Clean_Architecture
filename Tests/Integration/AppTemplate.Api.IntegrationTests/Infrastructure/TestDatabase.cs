using System.Data;
using System.Data.Common;
using System.Globalization;
using AppTemplate.Infrastructure.Persistence.Common.Contexts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AppTemplate.Api.IntegrationTests.Infrastructure;

/// <summary>
/// Direct access to the container's database, for the handful of assertions that have to look at
/// what was actually stored rather than at what an endpoint said.
/// </summary>
/// <remarks>
/// <para>
/// It borrows a connection from a <see cref="DbContext"/> instead of opening one of its own, so the
/// suite needs no Npgsql dependency and cannot drift from the connection string the host is using.
/// Commands go through <see cref="DbCommand"/> rather than <c>ExecuteSqlRaw</c>: the statements here
/// are assembled from schema metadata, and EF's analyser is right to object to that shape.
/// </para>
/// <para>
/// The reset strategy is <b>truncate, not transaction-rollback</b>. Each test drives several HTTP
/// requests, each of which gets its own scope, its own context and — for identity writes — its own
/// commit, so there is no single ambient transaction a test could roll back. Truncating every table
/// in every module schema between tests is the only reset that matches how the system actually
/// commits, and it also clears rows written by <c>SaveChanges</c> calls the test never saw.
/// </para>
/// </remarks>
public sealed class TestDatabase(IServiceProvider rootServices)
{
    private string? _truncateStatement;

    /// <summary>
    /// Builds the reset statement once, from <c>information_schema</c>. Discovered rather than
    /// hard-coded, so a table added to any of these schemas in future is truncated too instead of
    /// leaking silently from one test into the next.
    /// <para>
    /// The migrations history table is excluded by being outside every module schema: it lives in the
    /// connection's default schema because it belongs to none of them. Truncating it would make the
    /// next test run re-apply every migration.
    /// </para>
    /// </summary>
    public async Task PrepareAsync(CancellationToken cancellationToken)
    {
        var tables = await QueryAsync(
            $"""
            SELECT '"' || table_schema || '"."' || table_name || '"'
            FROM information_schema.tables
            WHERE table_schema IN (
                '{AppDbContext.IdentitySchema}', '{AppDbContext.TodoSchema}', '{AppDbContext.PlatformSchema}')
              AND table_type = 'BASE TABLE'
              AND table_name <> '{AppDbContext.MigrationsHistoryTableName}'
            ORDER BY table_schema, table_name
            """,
            cancellationToken);

        if (tables.Count == 0)
        {
            throw new InvalidOperationException(
                "No feature tables were found. The migrations did not run against the container.");
        }

        _truncateStatement =
            $"TRUNCATE TABLE {string.Join(", ", tables)} RESTART IDENTITY CASCADE";
    }

    public async Task ResetAsync(CancellationToken cancellationToken)
    {
        string statement = _truncateStatement
            ?? throw new InvalidOperationException($"{nameof(PrepareAsync)} has not been called.");

        await ExecuteAsync(statement, cancellationToken);
    }

    /// <summary>Every value of the first column of a result set, as text.</summary>
    public async Task<IReadOnlyList<string>> QueryAsync(string sql, CancellationToken cancellationToken)
    {
        await using var scope = rootServices.CreateAsyncScope();
        await using var command = await CreateCommandAsync(scope.ServiceProvider, sql, cancellationToken);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        var values = new List<string>();

        while (await reader.ReadAsync(cancellationToken))
        {
            values.Add(reader.IsDBNull(0)
                ? string.Empty
                : Convert.ToString(reader.GetValue(0), CultureInfo.InvariantCulture) ?? string.Empty);
        }

        return values;
    }

    public async Task<int> CountAsync(string sql, CancellationToken cancellationToken)
    {
        var values = await QueryAsync(sql, cancellationToken);

        return values.Count == 0 ? 0 : int.Parse(values[0], CultureInfo.InvariantCulture);
    }

    private async Task ExecuteAsync(string sql, CancellationToken cancellationToken)
    {
        await using var scope = rootServices.CreateAsyncScope();
        await using var command = await CreateCommandAsync(scope.ServiceProvider, sql, cancellationToken);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<DbCommand> CreateCommandAsync(
        IServiceProvider services,
        string sql,
        CancellationToken cancellationToken)
    {
        var connection = services.GetRequiredService<AppDbContext>().Database.GetDbConnection();

        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
        }

        var command = connection.CreateCommand();
        command.CommandText = sql;

        return command;
    }
}

using System.Data;
using System.Data.Common;
using System.Globalization;
//#if (localAuth)
using AppTemplate.Infrastructure.Auth.Common.Contexts;
//#endif
using AppTemplate.Infrastructure.Persistence.Common.Contexts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AppTemplate.Api.IntegrationTests.Infrastructure;

public sealed class TestDatabase(IServiceProvider rootServices)
{
    private const string _dataProtectionKeysTableName = "DataProtectionKeys";

    private string? _resetStatement;

    public async Task PrepareAsync(CancellationToken cancellationToken)
    {
        string[] schemas =
        [
            //#if (localAuth)
            AuthDbContext.IdentitySchema,
            //#endif
            //#if (todoLists)
            AppDbContext.TodoSchema,
            //#endif
            //#if (reminders)
            AppDbContext.RemindersSchema,
            //#endif
            //#if (files)
            AppDbContext.FilesSchema,
            //#endif
            //#if (notes)
            AppDbContext.NotesSchema,
            //#endif
            //#if (externalAuth)
            AppDbContext.ExternalIdentitySchema,
            //#endif
            AppDbContext.PlatformSchema,
        ];

        var tables = await QueryAsync(
            DatabaseServer.TablesSql(schemas, [AppDbContext.MigrationsHistoryTableName, _dataProtectionKeysTableName]),
            cancellationToken);

        if (tables.Count == 0)
        {
            throw new InvalidOperationException(
                "No feature tables were found. The migrations did not run against the container.");
        }

        _resetStatement = DatabaseServer.ResetSql(tables);
    }

    public async Task ResetAsync(CancellationToken cancellationToken)
    {
        string statement = _resetStatement
            ?? throw new InvalidOperationException($"{nameof(PrepareAsync)} has not been called.");

        await ExecuteAsync(statement, cancellationToken);
    }

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

    public async Task ExecuteAsync(string sql, CancellationToken cancellationToken)
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

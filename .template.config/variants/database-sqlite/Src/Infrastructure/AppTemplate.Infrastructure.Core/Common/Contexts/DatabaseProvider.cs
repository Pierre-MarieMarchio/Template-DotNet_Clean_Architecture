using System.Globalization;
using System.Linq.Expressions;
using AppTemplate.Infrastructure.Core.Common.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Npgsql;

namespace AppTemplate.Infrastructure.Core.Common.Contexts;

public static class DatabaseProvider
{
    public const string DesignTimeConnectionString =
        "Host=localhost;Port=5432;Database=appdb;Username=postgres;Password=postgres";

    public const string VersionColumnName = "xmin";

    public static TBuilder UseDatabase<TBuilder>(
        this TBuilder options,
        string connectionString,
        ContextMigrations? migrations = null)
        where TBuilder : DbContextOptionsBuilder
    {
        ArgumentNullException.ThrowIfNull(options);

        options.UseNpgsql(connectionString, npgsql =>
        {
            if (migrations is not null)
            {
                npgsql.MigrationsHistoryTable(migrations.HistoryTable, migrations.HistorySchema);
            }
        });

        return options;
    }

    public static TBuilder UseDatabase<TBuilder>(
        this TBuilder options,
        string connectionString,
        DatabaseOptions database,
        ContextMigrations migrations)
        where TBuilder : DbContextOptionsBuilder
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(migrations);

        options.UseNpgsql(
            new NpgsqlConnectionStringBuilder(connectionString) { MaxPoolSize = database.MaxPoolSize }.ConnectionString,
            npgsql => npgsql
                .EnableRetryOnFailure(
                    maxRetryCount: 5,
                    maxRetryDelay: TimeSpan.FromSeconds(10),
                    errorCodesToAdd: null)
                .CommandTimeout(database.CommandTimeoutSeconds)
                .MigrationsAssembly(migrations.Assembly)
                .MigrationsHistoryTable(migrations.HistoryTable, migrations.HistorySchema));

        return options;
    }

    public static ModelConfigurationBuilder ApplyDatabaseConventions(this ModelConfigurationBuilder configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        return configuration;
    }

    public static PropertyBuilder<uint> IsVersion(this PropertyBuilder<uint> property)
    {
        ArgumentNullException.ThrowIfNull(property);

        return property
            .HasColumnName(VersionColumnName)
            .HasColumnType("xid")
            .ValueGeneratedOnAddOrUpdate()
            .IsConcurrencyToken();
    }

    public static IndexBuilder<TEntity> IncludingColumns<TEntity>(
        this IndexBuilder<TEntity> index,
        Expression<Func<TEntity, object?>> columns)
    {
        ArgumentNullException.ThrowIfNull(index);

        return index.IncludeProperties(columns);
    }

    public static IndexBuilder<TEntity> WhereColumnEquals<TEntity>(
        this IndexBuilder<TEntity> index,
        string column,
        int value)
    {
        ArgumentNullException.ThrowIfNull(index);

        return index.HasFilter(string.Create(CultureInfo.InvariantCulture, $"\"{column}\" = {value}"));
    }

    public static bool IsUniqueViolation(DbUpdateException exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        return exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };
    }

    public static string DedicatedConnectionString(string connectionString) =>
        new NpgsqlConnectionStringBuilder(connectionString) { Pooling = false }.ConnectionString;
}

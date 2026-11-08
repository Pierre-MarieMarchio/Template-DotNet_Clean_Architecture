using System.Globalization;
using System.Linq.Expressions;
using AppTemplate.Infrastructure.Core.Common.Options;
using AppTemplate.Infrastructure.Core.Common.Saving.Versioning;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AppTemplate.Infrastructure.Core.Common.Contexts;

public static class DatabaseProvider
{
    public const string DesignTimeConnectionString =
        "Server=localhost,1433;Database=appdb;TrustServerCertificate=True";

    public const string VersionColumnName = "Version";

    private const string _contextsHistorySchema = "public";

    private const string _sqlServerHistorySchema = "dbo";

    private const int _duplicateKeyIndex = 2601;

    private const int _duplicateKeyConstraint = 2627;

    public static TBuilder UseDatabase<TBuilder>(
        this TBuilder options,
        string connectionString,
        ContextMigrations? migrations = null)
        where TBuilder : DbContextOptionsBuilder
    {
        ArgumentNullException.ThrowIfNull(options);

        options.UseSqlServer(connectionString, sql =>
        {
            if (migrations is not null)
            {
                sql.MigrationsHistoryTable(migrations.HistoryTable, HistorySchemaOf(migrations));
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

        options.UseSqlServer(
            new SqlConnectionStringBuilder(connectionString) { MaxPoolSize = database.MaxPoolSize }.ConnectionString,
            sql => sql
                .EnableRetryOnFailure(
                    maxRetryCount: 5,
                    maxRetryDelay: TimeSpan.FromSeconds(10),
                    errorNumbersToAdd: null)
                .CommandTimeout(database.CommandTimeoutSeconds)
                .MigrationsAssembly(migrations.Assembly)
                .MigrationsHistoryTable(migrations.HistoryTable, HistorySchemaOf(migrations)));

        options.AddInterceptors(new VersionStampingSaveChangesInterceptor());

        return options;
    }

    public static ModelConfigurationBuilder ApplyDatabaseConventions(this ModelConfigurationBuilder configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        configuration.Conventions.Add(_ => new RedundantCascadePathsConvention());

        return configuration;
    }

    public static PropertyBuilder<uint> IsVersion(this PropertyBuilder<uint> property)
    {
        ArgumentNullException.ThrowIfNull(property);

        return property
            .HasColumnName(VersionColumnName)
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

        return index.HasFilter(string.Create(CultureInfo.InvariantCulture, $"[{column}] = {value}"));
    }

    public static bool IsUniqueViolation(DbUpdateException exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        return exception.InnerException is SqlException { Number: _duplicateKeyIndex or _duplicateKeyConstraint };
    }

    public static string DedicatedConnectionString(string connectionString) =>
        new SqlConnectionStringBuilder(connectionString) { Pooling = false }.ConnectionString;

    private static string HistorySchemaOf(ContextMigrations migrations) =>
        migrations.HistorySchema == _contextsHistorySchema ? _sqlServerHistorySchema : migrations.HistorySchema;
}

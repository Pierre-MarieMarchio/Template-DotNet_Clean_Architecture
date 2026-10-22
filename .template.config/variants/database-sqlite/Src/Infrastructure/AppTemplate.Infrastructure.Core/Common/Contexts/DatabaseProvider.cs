using System.Globalization;
using System.Linq.Expressions;
using AppTemplate.Infrastructure.Core.Common.Options;
using AppTemplate.Infrastructure.Core.Common.Saving.Versioning;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace AppTemplate.Infrastructure.Core.Common.Contexts;

public static class DatabaseProvider
{
    public const string DesignTimeConnectionString = "Data Source=App_Data/appdb.db";

    public const string VersionColumnName = "Version";

    private const int _constraintViolation = 19;

    private const int _uniqueViolation = 2067;

    private const int _primaryKeyViolation = 1555;

    private static readonly VersionStampingSaveChangesInterceptor _versionStamping = new();

    public static TBuilder UseDatabase<TBuilder>(
        this TBuilder options,
        string connectionString,
        ContextMigrations? migrations = null)
        where TBuilder : DbContextOptionsBuilder
    {
        ArgumentNullException.ThrowIfNull(options);

        options.UseSqlite(connectionString, sqlite =>
        {
            if (migrations is not null)
            {
                sqlite.MigrationsHistoryTable(HistoryTableOf(migrations));
            }
        });

        options.AddInterceptors(SqliteDatabaseFile.Interceptor);

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

        options.UseSqlite(
            new SqliteConnectionStringBuilder(connectionString) { DefaultTimeout = database.CommandTimeoutSeconds }.ToString(),
            sqlite => sqlite
                .ExecutionStrategy(dependencies => new SqliteBusyRetryStrategy(dependencies))
                .CommandTimeout(database.CommandTimeoutSeconds)
                .MigrationsAssembly(migrations.Assembly)
                .MigrationsHistoryTable(HistoryTableOf(migrations)));

        options.AddInterceptors(SqliteDatabaseFile.Interceptor, _versionStamping);

        return options;
    }

    public static ModelConfigurationBuilder ApplyDatabaseConventions(this ModelConfigurationBuilder configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        configuration.Conventions.Add(_ => new SchemaAsTablePrefixConvention());

        configuration.Properties<DateTimeOffset>().HaveConversion<DateTimeOffsetToBinaryConverter>();
        configuration.Properties<DateTimeOffset?>().HaveConversion<DateTimeOffsetToBinaryConverter>();

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
        ArgumentNullException.ThrowIfNull(columns);

        return index;
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

        return exception.InnerException is SqliteException
        {
            SqliteErrorCode: _constraintViolation,
            SqliteExtendedErrorCode: _uniqueViolation or _primaryKeyViolation,
        };
    }

    public static string DedicatedConnectionString(string connectionString) =>
        new SqliteConnectionStringBuilder(connectionString) { Pooling = false }.ToString();

    private static string HistoryTableOf(ContextMigrations migrations) =>
        SchemaAsTablePrefixConvention.TableName(migrations.HistorySchema, migrations.HistoryTable);
}

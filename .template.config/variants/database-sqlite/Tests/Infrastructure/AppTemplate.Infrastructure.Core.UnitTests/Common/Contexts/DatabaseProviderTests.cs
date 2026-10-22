using System.Globalization;
using AppTemplate.Infrastructure.Core.Common.Contexts;
using AppTemplate.Infrastructure.Core.Common.Options;
using AppTemplate.Infrastructure.Core.Common.Saving.Versioning;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Shouldly;
using Xunit;

namespace AppTemplate.Infrastructure.Core.UnitTests.Common.Contexts;

public sealed class DatabaseProviderTests
{
    private static readonly ContextMigrations _migrations = new("Some.Assembly", "__History", "history");

    [Fact]
    public void TheVersion_IsACounterColumn_TheSaveStamps()
    {
        using var context = new ProbeContext();
        var version = context.Model.FindEntityType(typeof(ProbeRow))!.FindProperty(nameof(ProbeRow.Version))!;

        version.GetColumnName().ShouldBe(VersionStampingSaveChangesInterceptor.VersionPropertyName);
        version.ValueGenerated.ShouldBe(ValueGenerated.Never);
        version.IsConcurrencyToken.ShouldBeTrue();
    }

    [Fact]
    public void ACoveringIndex_KeepsOnlyItsKey_AndAFilteredIndex_ComparesTheQuotedColumn()
    {
        using var context = new ProbeContext();
        var indexes = context.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(ProbeRow))!.GetIndexes().ToList();

        indexes.Single(index => index.GetDatabaseName() == "IX_Covering").Properties
            .Select(property => property.Name)
            .ShouldBe([nameof(ProbeRow.State)]);
        indexes.Single(index => index.GetDatabaseName() == "IX_Filtered").GetFilter()
            .ShouldBe("\"State\" = 2");
    }

    [Fact]
    public void AtRunTime_TheContextWaitsOutABusyFile_RetriesAndWritesItsOwnHistory()
    {
        var options = new DbContextOptionsBuilder()
            .UseDatabase(
                DatabaseProvider.DesignTimeConnectionString,
                new DatabaseOptions { MaxPoolSize = 37, CommandTimeoutSeconds = 12 },
                _migrations)
            .Options;
        var relational = RelationalOptionsExtension.Extract(options);

        new SqliteConnectionStringBuilder(relational.ConnectionString).DefaultTimeout.ShouldBe(12);
        relational.CommandTimeout.ShouldBe(12);
        relational.ExecutionStrategyFactory.ShouldNotBeNull();
        relational.MigrationsAssembly.ShouldBe(_migrations.Assembly);
        relational.MigrationsHistoryTableName.ShouldBe("history___History");
        relational.MigrationsHistoryTableSchema.ShouldBeNull();
    }

    [Fact]
    public void AtRunTime_TheSaveStampsTheVersion()
    {
        var options = new DbContextOptionsBuilder()
            .UseDatabase(DatabaseProvider.DesignTimeConnectionString, new DatabaseOptions(), _migrations)
            .Options;

        options.FindExtension<CoreOptionsExtension>()!.Interceptors!
            .OfType<VersionStampingSaveChangesInterceptor>()
            .ShouldHaveSingleItem();
    }

    [Fact]
    public async Task AtRunTime_ABusyFileIsRetried_AndNothingElseIs()
    {
        await using var context = new ProbeContext(new DbContextOptionsBuilder<ProbeContext>()
            .UseDatabase(DatabaseProvider.DesignTimeConnectionString, new DatabaseOptions(), _migrations)
            .Options);
        var strategy = context.Database.CreateExecutionStrategy();
        int busyAttempts = 0;
        int violatingAttempts = 0;

        await strategy.ExecuteAsync(async () =>
        {
            await Task.Yield();

            if (++busyAttempts == 1)
            {
                throw new SqliteException("database is locked", 5);
            }
        });

        await Should.ThrowAsync<SqliteException>(() => strategy.ExecuteAsync(async () =>
        {
            await Task.Yield();
            violatingAttempts++;

            throw new SqliteException("UNIQUE constraint failed", 19, 2067);
        }));

        busyAttempts.ShouldBe(2);
        violatingAttempts.ShouldBe(1);
    }

    [Fact]
    public void AtDesignTime_TheContextWritesItsOwnHistory()
    {
        var relational = RelationalOptionsExtension.Extract(new DbContextOptionsBuilder()
            .UseDatabase(DatabaseProvider.DesignTimeConnectionString, _migrations)
            .Options);

        relational.MigrationsHistoryTableName.ShouldBe("history___History");
        relational.MigrationsHistoryTableSchema.ShouldBeNull();
    }

    [Fact]
    public void OnlyAUniqueOrPrimaryKeyViolation_IsReportedAsOne()
    {
        DatabaseProvider.IsUniqueViolation(new DbUpdateException("unique", new SqliteException("unique", 19, 2067)))
            .ShouldBeTrue();

        DatabaseProvider.IsUniqueViolation(new DbUpdateException("primary key", new SqliteException("primary key", 19, 1555)))
            .ShouldBeTrue();

        DatabaseProvider.IsUniqueViolation(new DbUpdateException("foreign key", new SqliteException("foreign key", 19, 787)))
            .ShouldBeFalse();

        DatabaseProvider.IsUniqueViolation(new DbUpdateException("busy", new SqliteException("busy", 5, 5)))
            .ShouldBeFalse();

        DatabaseProvider.IsUniqueViolation(new DbUpdateException("no inner exception")).ShouldBeFalse();
    }

    [Fact]
    public void ADedicatedConnection_IsNeverTakenFromThePool() =>
        new SqliteConnectionStringBuilder(DatabaseProvider.DedicatedConnectionString(DatabaseProvider.DesignTimeConnectionString))
            .Pooling.ShouldBeFalse();

    [Fact]
    public void TheConventions_PrefixEachTableWithItsSchema()
    {
        using var context = new ProbeContext();
        var row = context.Model.FindEntityType(typeof(ProbeRow))!;

        row.GetTableName().ShouldBe("probe_ProbeRows");
        row.GetSchema().ShouldBeNull();
    }

    [Fact]
    public void TheConventions_StoreEveryDateTimeOffsetAsAnOrderedNumber()
    {
        using var context = new ProbeContext();
        var row = context.Model.FindEntityType(typeof(ProbeRow))!;

        row.FindProperty(nameof(ProbeRow.CreatedAt))!.GetValueConverter().ShouldBeOfType<DateTimeOffsetToBinaryConverter>();
        row.FindProperty(nameof(ProbeRow.DeletedAt))!.GetValueConverter().ShouldBeOfType<DateTimeOffsetToBinaryConverter>();
    }

    [Fact]
    public async Task ADateTimeOffset_IsComparedAndOrderedByTheDatabase()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var context = new ProbeContext(connection);
        await context.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);

        var start = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        context.AddRange(
            new ProbeRow { Id = Guid.CreateVersion7(), CreatedAt = start.AddMinutes(2) },
            new ProbeRow { Id = Guid.CreateVersion7(), CreatedAt = start },
            new ProbeRow { Id = Guid.CreateVersion7(), CreatedAt = start.AddMinutes(1) });
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var later = await context.Set<ProbeRow>()
            .Where(probe => probe.CreatedAt > start)
            .OrderByDescending(probe => probe.CreatedAt)
            .Select(probe => probe.CreatedAt)
            .ToListAsync(TestContext.Current.CancellationToken);

        later.ShouldBe([start.AddMinutes(2), start.AddMinutes(1)]);
    }

    [Fact]
    public async Task TheDatabaseFile_GetsItsFolder_AndAWriteAheadLog()
    {
        string folder = Path.Combine(
            Path.GetTempPath(),
            string.Create(CultureInfo.InvariantCulture, $"database-provider-{Guid.CreateVersion7():N}"));
        string connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = Path.Combine(folder, "App_Data", "probe.db"),
            Pooling = false,
        }.ToString();

        try
        {
            await using var context = new ProbeContext(connectionString);

            await context.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
            var journal = await context.Database
                .SqlQueryRaw<string>("SELECT journal_mode AS \"Value\" FROM pragma_journal_mode")
                .SingleAsync(TestContext.Current.CancellationToken);

            journal.ShouldBe("wal");
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    private sealed class ProbeRow
    {
        public Guid Id { get; set; }

        public int State { get; set; }

        public long Size { get; set; }

        public uint Version { get; set; }

        public DateTimeOffset CreatedAt { get; set; }

        public DateTimeOffset? DeletedAt { get; set; }
    }

    private sealed class ProbeContext(DbContextOptions<ProbeContext> options) : DbContext(options)
    {
        public ProbeContext()
            : this(DatabaseProvider.DesignTimeConnectionString)
        {
        }

        public ProbeContext(string connectionString)
            : this(new DbContextOptionsBuilder<ProbeContext>().UseDatabase(connectionString).Options)
        {
        }

        public ProbeContext(SqliteConnection connection)
            : this(new DbContextOptionsBuilder<ProbeContext>().UseDatabase(DatabaseProvider.DesignTimeConnectionString).UseSqlite(connection).Options)
        {
        }

        protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) =>
            configurationBuilder.ApplyDatabaseConventions();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<ProbeRow>(row =>
            {
                row.ToTable("ProbeRows", "probe");
                row.Property(probe => probe.Version).IsVersion();
                row.HasIndex(probe => probe.State).IncludingColumns(probe => probe.Size).HasDatabaseName("IX_Covering");
                row.HasIndex(probe => probe.Id).WhereColumnEquals(nameof(ProbeRow.State), 2).HasDatabaseName("IX_Filtered");
            });
        }
    }
}

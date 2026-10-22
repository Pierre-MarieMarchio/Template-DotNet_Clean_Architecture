using AppTemplate.Infrastructure.Core.Common.Contexts;
using AppTemplate.Infrastructure.Core.Common.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Npgsql;
using Shouldly;
using Xunit;

namespace AppTemplate.Infrastructure.Core.UnitTests.Common.Contexts;

public sealed class DatabaseProviderTests
{
    private static readonly ContextMigrations _migrations = new("Some.Assembly", "__History", "history");

    [Fact]
    public void TheVersion_IsPostgreSqlsRowVersion_ReadBackAfterEverySave()
    {
        using var context = new ProbeContext();
        var version = context.Model.FindEntityType(typeof(ProbeRow))!.FindProperty(nameof(ProbeRow.Version))!;

        version.GetColumnName().ShouldBe("xmin");
        version.GetColumnType().ShouldBe("xid");
        version.ValueGenerated.ShouldBe(ValueGenerated.OnAddOrUpdate);
        version.IsConcurrencyToken.ShouldBeTrue();
    }

    [Fact]
    public void ACoveringIndex_IncludesItsColumns_AndAFilteredIndex_ComparesTheQuotedColumn()
    {
        using var context = new ProbeContext();
        var indexes = context.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(ProbeRow))!.GetIndexes().ToList();

        indexes.Single(index => index.GetDatabaseName() == "IX_Covering").GetIncludeProperties()
            .ShouldBe([nameof(ProbeRow.Size)]);
        indexes.Single(index => index.GetDatabaseName() == "IX_Filtered").GetFilter()
            .ShouldBe("\"State\" = 2");
    }

    [Fact]
    public void AtRunTime_TheContextCapsItsPool_RetriesAndWritesItsOwnHistory()
    {
        var options = new DbContextOptionsBuilder()
            .UseDatabase(
                DatabaseProvider.DesignTimeConnectionString,
                new DatabaseOptions { MaxPoolSize = 37, CommandTimeoutSeconds = 12 },
                _migrations)
            .Options;
        var relational = RelationalOptionsExtension.Extract(options);

        new NpgsqlConnectionStringBuilder(relational.ConnectionString).MaxPoolSize.ShouldBe(37);
        relational.CommandTimeout.ShouldBe(12);
        relational.ExecutionStrategyFactory.ShouldNotBeNull();
        relational.MigrationsAssembly.ShouldBe(_migrations.Assembly);
        relational.MigrationsHistoryTableName.ShouldBe(_migrations.HistoryTable);
        relational.MigrationsHistoryTableSchema.ShouldBe(_migrations.HistorySchema);
    }

    [Fact]
    public void AtDesignTime_TheContextWritesItsOwnHistory()
    {
        var relational = RelationalOptionsExtension.Extract(new DbContextOptionsBuilder()
            .UseDatabase(DatabaseProvider.DesignTimeConnectionString, _migrations)
            .Options);

        relational.MigrationsHistoryTableName.ShouldBe(_migrations.HistoryTable);
        relational.MigrationsHistoryTableSchema.ShouldBe(_migrations.HistorySchema);
    }

    [Fact]
    public void OnlyAUniqueViolation_IsReportedAsOne()
    {
        DatabaseProvider.IsUniqueViolation(new DbUpdateException(
            "duplicate key",
            new PostgresException("duplicate key", "ERROR", "ERROR", PostgresErrorCodes.UniqueViolation)))
            .ShouldBeTrue();

        DatabaseProvider.IsUniqueViolation(new DbUpdateException(
            "foreign key",
            new PostgresException("foreign key", "ERROR", "ERROR", PostgresErrorCodes.ForeignKeyViolation)))
            .ShouldBeFalse();

        DatabaseProvider.IsUniqueViolation(new DbUpdateException("no inner exception")).ShouldBeFalse();
    }

    [Fact]
    public void ADedicatedConnection_IsNeverTakenFromThePool() =>
        new NpgsqlConnectionStringBuilder(DatabaseProvider.DedicatedConnectionString(DatabaseProvider.DesignTimeConnectionString))
            .Pooling.ShouldBeFalse();

    [Fact]
    public void TheConventions_LeaveTheSchemasAsTheyAre()
    {
        using var context = new ProbeContext();

        context.Model.FindEntityType(typeof(ProbeRow))!.GetSchema().ShouldBe("probe");
    }

    private sealed class ProbeRow
    {
        public Guid Id { get; set; }

        public int State { get; set; }

        public long Size { get; set; }

        public uint Version { get; set; }
    }

    private sealed class ProbeContext() : DbContext(
        new DbContextOptionsBuilder<ProbeContext>().UseDatabase(DatabaseProvider.DesignTimeConnectionString).Options)
    {
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

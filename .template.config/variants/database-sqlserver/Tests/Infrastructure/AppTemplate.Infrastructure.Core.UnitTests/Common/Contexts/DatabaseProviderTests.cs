using System.Reflection;
using AppTemplate.Infrastructure.Core.Common.Contexts;
using AppTemplate.Infrastructure.Core.Common.Options;
using AppTemplate.Infrastructure.Core.Common.Saving.Versioning;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Shouldly;
using Xunit;

namespace AppTemplate.Infrastructure.Core.UnitTests.Common.Contexts;

public sealed class DatabaseProviderTests
{
    private const int _duplicateKeyIndex = 2601;

    private const int _duplicateKeyConstraint = 2627;

    private const int _foreignKeyViolation = 547;

    private static readonly ContextMigrations _migrations = new("Some.Assembly", "__History", "history");

    [Fact]
    public void TheVersion_IsAPlainConcurrencyToken_TheSavePipelineStamps()
    {
        using var context = new ProbeContext();
        var version = context.Model.FindEntityType(typeof(ProbeRow))!.FindProperty(nameof(ProbeRow.Version))!;

        version.GetColumnName().ShouldBe("Version");
        version.GetColumnType().ShouldBe("bigint");
        version.ValueGenerated.ShouldBe(ValueGenerated.Never);
        version.IsConcurrencyToken.ShouldBeTrue();
    }

    [Fact]
    public void ACoveringIndex_IncludesItsColumns_AndAFilteredIndex_ComparesTheBracketedColumn()
    {
        using var context = new ProbeContext();
        var indexes = context.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(ProbeRow))!.GetIndexes().ToList();

        indexes.Single(index => index.GetDatabaseName() == "IX_Covering").GetIncludeProperties()
            .ShouldBe([nameof(ProbeRow.Size)]);
        indexes.Single(index => index.GetDatabaseName() == "IX_Filtered").GetFilter()
            .ShouldBe("[State] = 2");
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

        new SqlConnectionStringBuilder(relational.ConnectionString).MaxPoolSize.ShouldBe(37);
        relational.CommandTimeout.ShouldBe(12);
        relational.ExecutionStrategyFactory.ShouldNotBeNull();
        relational.MigrationsAssembly.ShouldBe(_migrations.Assembly);
        relational.MigrationsHistoryTableName.ShouldBe(_migrations.HistoryTable);
        relational.MigrationsHistoryTableSchema.ShouldBe(_migrations.HistorySchema);
    }

    [Fact]
    public void AtRunTime_TheContextStampsItsVersions()
    {
        var options = new DbContextOptionsBuilder()
            .UseDatabase(DatabaseProvider.DesignTimeConnectionString, new DatabaseOptions(), _migrations)
            .Options;

        options.FindExtension<CoreOptionsExtension>()!.Interceptors!
            .OfType<VersionStampingSaveChangesInterceptor>()
            .ShouldHaveSingleItem();
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
    public void TheHistoryOfTheDefaultSchema_GoesToDbo()
    {
        var relational = RelationalOptionsExtension.Extract(new DbContextOptionsBuilder()
            .UseDatabase(DatabaseProvider.DesignTimeConnectionString, _migrations with { HistorySchema = "public" })
            .Options);

        relational.MigrationsHistoryTableSchema.ShouldBe("dbo");
    }

    [Theory]
    [InlineData(_duplicateKeyIndex, true)]
    [InlineData(_duplicateKeyConstraint, true)]
    [InlineData(_foreignKeyViolation, false)]
    public void OnlyADuplicateKey_IsReportedAsAUniqueViolation(int number, bool expected) =>
        DatabaseProvider.IsUniqueViolation(new DbUpdateException("failed", ASqlException(number))).ShouldBe(expected);

    [Fact]
    public void AnUpdateWithoutAnInnerException_IsNotAUniqueViolation() =>
        DatabaseProvider.IsUniqueViolation(new DbUpdateException("no inner exception")).ShouldBeFalse();

    [Fact]
    public void ADedicatedConnection_IsNeverTakenFromThePool() =>
        new SqlConnectionStringBuilder(DatabaseProvider.DedicatedConnectionString(DatabaseProvider.DesignTimeConnectionString))
            .Pooling.ShouldBeFalse();

    [Fact]
    public void TheConventions_LeaveTheSchemasAsTheyAre()
    {
        using var context = new ProbeContext();

        context.Model.FindEntityType(typeof(ProbeRow))!.GetSchema().ShouldBe("probe");
    }

    private static SqlException ASqlException(int number)
    {
        const BindingFlags nonPublic = BindingFlags.NonPublic | BindingFlags.Instance;

        var error = typeof(SqlError).GetConstructors(nonPublic)
            .Single(constructor => constructor.GetParameters().Length == 8)
            .Invoke([number, (byte)0, (byte)0, "server", "failed", "procedure", 0, null]);

        var errors = (SqlErrorCollection)Activator.CreateInstance(typeof(SqlErrorCollection), nonPublic: true)!;
        typeof(SqlErrorCollection).GetMethod("Add", nonPublic)!.Invoke(errors, [error]);

        return (SqlException)typeof(SqlException)
            .GetMethod("CreateException", BindingFlags.NonPublic | BindingFlags.Static, [typeof(SqlErrorCollection), typeof(string)])!
            .Invoke(null, [errors, "16.0"])!;
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

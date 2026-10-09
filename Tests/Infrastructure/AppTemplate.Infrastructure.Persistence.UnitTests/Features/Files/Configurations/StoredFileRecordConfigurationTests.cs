using AppTemplate.Domain.Features.Files.ValueObjects;
using AppTemplate.Infrastructure.Persistence.Common.Contexts;
using AppTemplate.Infrastructure.Persistence.Features.Files.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Shouldly;
using Xunit;

namespace AppTemplate.Infrastructure.Persistence.UnitTests.Features.Files.Configurations;

/// <summary>
/// Assertions against the built EF model. No database is contacted: the provider is only there so the
/// model carries the same relational mapping the real one has.
/// <para>
/// These are here because two of the schema's properties are load-bearing in a way the migration file
/// does not explain, and a future edit that quietly drops either would look harmless in review.
/// </para>
/// </summary>
public sealed class StoredFileRecordConfigurationTests
{
    /// <summary>
    /// <b>Uniqueness on the object key is a safety guarantee, not a lookup optimisation.</b> A file's
    /// bytes are reclaimed by deleting every object no row names, so two rows sharing a key would make
    /// deleting either of them reclaim the content of both: the survivor keeps a row pointing at bytes
    /// that are gone. Nothing in the application prevents that state — only this index does.
    /// </summary>
    [Fact]
    public void TheObjectKey_IsUnique()
    {
        var index = ObjectKeyIndex();

        index.ShouldNotBeNull(
            "no index covers ObjectKey alone. Deleting one of two rows that share a key would reclaim "
            + "the other's bytes, and nothing else in this system makes that unreachable.");

        index.IsUnique.ShouldBeTrue(
            "the index over ObjectKey exists but no longer enforces uniqueness, so two rows may name "
            + "one object again.");
    }

    /// <summary>
    /// The column has to hold every key the domain will mint. Shorter, and a legitimate registration
    /// fails at write time on a value the aggregate had already accepted.
    /// </summary>
    [Theory]
    [InlineData(nameof(StoredFileRecord.Category), FileCategory.MaxLength)]
    [InlineData(nameof(StoredFileRecord.ObjectKey), ObjectKey.MaxLength)]
    [InlineData(nameof(StoredFileRecord.Name), StoredFileName.MaxLength)]
    [InlineData(nameof(StoredFileRecord.DeclaredMediaType), DeclaredMediaType.MaxLength)]
    [InlineData(nameof(StoredFileRecord.Checksum), Sha256Checksum.Length)]
    public void EveryTextColumn_IsAsLongAsTheDomainAllows(string property, int expected)
    {
        Entity().FindProperty(property)!.GetMaxLength().ShouldBe(
            expected,
            $"the column for {property} and the invariant behind it have drifted apart. Read the length "
            + "from the domain's own constant so they cannot.");
    }

    /// <summary>
    /// The concurrency token is PostgreSQL's own <c>xmin</c>, read back after every write and put into
    /// the <c>WHERE</c> clause of the next one. A row without it silently accepts a lost update.
    /// </summary>
    [Fact]
    public void TheVersion_IsTheDatabasesOwnConcurrencyToken()
    {
        var version = Entity().FindProperty(nameof(StoredFileRecord.Version))!;

        version.IsConcurrencyToken.ShouldBeTrue();
        version.GetColumnName().ShouldBe("xmin");
    }

    /// <summary>
    /// The feature's own schema, not <c>platform</c> and not another feature's: a table that names its
    /// schema cannot drift into the wrong one by omission, and a feature that owns one can be removed by
    /// deleting a migration.
    /// </summary>
    [Fact]
    public void TheTable_LivesInTheFeaturesOwnSchema()
    {
        Entity().GetSchema().ShouldBe(AppDbContext.FilesSchema);
        Entity().GetTableName().ShouldBe("StoredFiles");
    }

    /// <summary>
    /// The quota read sums one owner's rows in one category by state; the index leads with the two
    /// equality filters and carries the size, so the sums never touch the table.
    /// </summary>
    [Fact]
    public void TheQuotaRead_IsServedByAnIndexOverOwnerCategoryAndState()
    {
        var index = DesignTimeEntity().GetIndexes().SingleOrDefault(candidate =>
            candidate.Properties.Select(property => property.Name).SequenceEqual(
                [nameof(StoredFileRecord.OwnerId), nameof(StoredFileRecord.Category), nameof(StoredFileRecord.State)]));

        index.ShouldNotBeNull();
        index.GetIncludeProperties().ShouldBe([nameof(StoredFileRecord.SizeInBytes)]);
    }

    /// <summary>
    /// The inspection queue is read through a partial index holding deposited rows only, so the due
    /// filter and its sort never scan the files already decided.
    /// </summary>
    [Fact]
    public void TheInspectionQueue_IsServedByAPartialIndexOverDepositedRows()
    {
        var index = Entity().GetIndexes().SingleOrDefault(candidate =>
            candidate.Properties.Select(property => property.Name).SequenceEqual(
                [nameof(StoredFileRecord.NextInspectionAt), nameof(StoredFileRecord.RegisteredAt), nameof(StoredFileRecord.Id)]));

        index.ShouldNotBeNull();
        index.GetFilter().ShouldBe($"\"State\" = {(int)StoredFileState.Deposited}");
    }

    [Fact]
    public void TheOrphanSweepCheckpoint_IsOneRowPerWalkInTheFeaturesOwnSchema()
    {
        using var context = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>()
                .UseNpgsql("Host=localhost;Database=never-opened;Username=none;Password=none")
                .Options);

        var entity = context.Model.FindEntityType(typeof(ContentSweepCheckpointRecord))!;

        entity.GetSchema().ShouldBe(AppDbContext.FilesSchema);
        entity.GetTableName().ShouldBe("ContentSweepCheckpoints");
        entity.FindPrimaryKey()!.Properties.Select(property => property.Name)
            .ShouldBe([nameof(ContentSweepCheckpointRecord.Name)]);
        entity.FindProperty(nameof(ContentSweepCheckpointRecord.LastKey))!.IsNullable.ShouldBeTrue();
        entity.FindProperty(nameof(ContentSweepCheckpointRecord.LastKey))!.GetMaxLength().ShouldBe(ObjectKey.MaxLength);
    }

    [Fact]
    public void TheCategory_IsRequired() =>
        Entity().FindProperty(nameof(StoredFileRecord.Category))!.IsNullable.ShouldBeFalse();

    private static IIndex? ObjectKeyIndex() =>
        Entity().GetIndexes().FirstOrDefault(index =>
            index.Properties.Count == 1
            && index.Properties[0].Name == nameof(StoredFileRecord.ObjectKey));

    /// <summary>The model with the annotations only migrations read, such as an index's included columns.</summary>
    private static IEntityType DesignTimeEntity()
    {
        using var context = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>()
                .UseNpgsql("Host=localhost;Database=never-opened;Username=none;Password=none")
                .Options);

        return context.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(StoredFileRecord))!;
    }

    private static IEntityType Entity()
    {
        using var context = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>()
                .UseNpgsql("Host=localhost;Database=never-opened;Username=none;Password=none")
                .Options);

        return context.Model.FindEntityType(typeof(StoredFileRecord))!;
    }
}

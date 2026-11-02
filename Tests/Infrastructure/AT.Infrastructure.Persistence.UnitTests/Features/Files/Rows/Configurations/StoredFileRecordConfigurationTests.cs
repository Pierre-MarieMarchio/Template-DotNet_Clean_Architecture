using AppTemplate.Domain.Features.Files.Model.ValueObjects;
using AppTemplate.Infrastructure.Core.Common.Contexts;
using AppTemplate.Infrastructure.Persistence.Common.Contexts;
using AppTemplate.Infrastructure.Persistence.Features.Files.Rows.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Shouldly;
using Xunit;

namespace AppTemplate.Infrastructure.Persistence.UnitTests.Features.Files.Rows.Configurations;

public sealed class StoredFileRecordConfigurationTests
{
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

    [Fact]
    public void TheVersion_IsTheDatabasesOwnConcurrencyToken()
    {
        var version = Entity().FindProperty(nameof(StoredFileRecord.Version))!;

        version.IsConcurrencyToken.ShouldBeTrue();
        version.GetColumnName().ShouldBe(DatabaseProvider.VersionColumnName);
    }

    [Fact]
    public void TheTable_LivesInTheFeaturesOwnSchema()
    {
        Entity().GetSchema().ShouldBe(AppDbContext.FilesSchema);
        Entity().GetTableName().ShouldBe("StoredFiles");
    }

    [Fact]
    public void TheQuotaRead_IsServedByAnIndexOverOwnerCategoryAndState()
    {
        var index = DesignTimeEntity().GetIndexes().SingleOrDefault(candidate =>
            candidate.Properties.Select(property => property.Name).SequenceEqual(
                [nameof(StoredFileRecord.OwnerId), nameof(StoredFileRecord.Category), nameof(StoredFileRecord.State)]));

        index.ShouldNotBeNull();
        index.GetIncludeProperties().ShouldBe([nameof(StoredFileRecord.SizeInBytes)]);
    }

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
                .UseDatabase(DatabaseProvider.DesignTimeConnectionString)
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

    private static IEntityType DesignTimeEntity()
    {
        using var context = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>()
                .UseDatabase(DatabaseProvider.DesignTimeConnectionString)
                .Options);

        return context.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(StoredFileRecord))!;
    }

    private static IEntityType Entity()
    {
        using var context = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>()
                .UseDatabase(DatabaseProvider.DesignTimeConnectionString)
                .Options);

        return context.Model.FindEntityType(typeof(StoredFileRecord))!;
    }
}

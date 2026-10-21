using AppTemplate.Infrastructure.Core.Common.Contexts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Shouldly;
using Xunit;

namespace AppTemplate.Infrastructure.Core.UnitTests.Common.Contexts;

public sealed class SchemaAsTablePrefixConventionTests
{
    [Fact]
    public void ATableInASchema_TakesTheSchemaAsAPrefix()
    {
        var entity = Entity<RowInASchema>();

        entity.GetSchema().ShouldBeNull();
        entity.GetTableName().ShouldBe("todo_Rows");
    }

    [Fact]
    public void ATableInTheDefaultSchema_TakesTheDefaultSchemaAsAPrefix()
    {
        var entity = Entity<RowInTheDefaultSchema>();

        entity.GetSchema().ShouldBeNull();
        entity.GetTableName().ShouldBe("platform_Plain");
    }

    [Fact]
    public void AnOwnedTypeInItsOwnersTable_FollowsItsOwner()
    {
        var owned = Model().FindEntityType(typeof(OwnedPart))!;

        owned.GetTableName().ShouldBe("todo_Rows");
        owned.GetSchema().ShouldBeNull();
    }

    [Fact]
    public void TheDefaultSchema_IsCleared() =>
        Model().GetDefaultSchema().ShouldBeNull();

    [Fact]
    public void TheHistoryTableName_FollowsTheSameRule() =>
        SchemaAsTablePrefixConvention.TableName("identity", "__EFMigrationsHistory").ShouldBe("identity___EFMigrationsHistory");

    private static IEntityType Entity<TEntity>() => Model().FindEntityType(typeof(TEntity))!;

    private static IModel Model()
    {
        using var context = new PrefixedContext();

        return context.Model;
    }

    private sealed class RowInASchema
    {
        public Guid Id { get; set; }

        public OwnedPart Part { get; set; } = new();
    }

    private sealed class OwnedPart
    {
        public string? Note { get; set; }
    }

    private sealed class RowInTheDefaultSchema
    {
        public Guid Id { get; set; }
    }

    private sealed class PrefixedContext() : DbContext(
        new DbContextOptionsBuilder<PrefixedContext>().UseSqlite("Data Source=:memory:").Options)
    {
        protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) =>
            configurationBuilder.Conventions.Add(_ => new SchemaAsTablePrefixConvention());

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.HasDefaultSchema("platform");
            modelBuilder.Entity<RowInASchema>(row =>
            {
                row.ToTable("Rows", "todo");
                row.OwnsOne(candidate => candidate.Part);
            });
            modelBuilder.Entity<RowInTheDefaultSchema>().ToTable("Plain");
        }
    }
}

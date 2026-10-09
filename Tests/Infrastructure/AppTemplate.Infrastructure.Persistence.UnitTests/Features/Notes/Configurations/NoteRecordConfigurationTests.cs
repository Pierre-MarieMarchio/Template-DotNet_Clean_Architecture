using AppTemplate.Application.Features.Notes.Policies;
using AppTemplate.Infrastructure.Persistence.Common.Contexts;
using AppTemplate.Infrastructure.Persistence.Features.Notes.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Shouldly;
using Xunit;

namespace AppTemplate.Infrastructure.Persistence.UnitTests.Features.Notes.Configurations;

/// <summary>
/// Assertions against the built EF model. No database is contacted: the provider is only there so the
/// model carries the same relational mapping the real one has.
/// </summary>
public sealed class NoteRecordConfigurationTests
{
    /// <summary>
    /// The concurrency token is PostgreSQL's own <c>xmin</c>, read back after every write and put into
    /// the <c>WHERE</c> clause of the next UPDATE or DELETE. A row without it accepts a lost update.
    /// </summary>
    [Fact]
    public void TheVersion_IsTheDatabasesOwnConcurrencyToken()
    {
        var version = Entity().FindProperty(nameof(NoteRecord.Version))!;

        version.IsConcurrencyToken.ShouldBeTrue();
        version.GetColumnName().ShouldBe("xmin");
        version.GetColumnType().ShouldBe("xid");
    }

    [Theory]
    [InlineData(nameof(NoteRecord.Title), NoteInputPolicy.TitleMaxLength)]
    [InlineData(nameof(NoteRecord.Body), NoteInputPolicy.BodyMaxLength)]
    public void EveryTextColumn_IsAsLongAsTheValidatorAllows(string property, int expected) =>
        Entity().FindProperty(property)!.GetMaxLength().ShouldBe(expected);

    /// <summary>
    /// Every read filters by owner, so every index that serves a sortable field leads with it and ends
    /// in the id the sort map appends as its tiebreaker.
    /// </summary>
    [Fact]
    public void EverySortIndex_LeadsWithTheOwner_AndEndsWithTheId()
    {
        var indexes = Entity().GetIndexes().ToList();

        indexes.Count.ShouldBe(NoteCollectionPolicy.Instance.SortableFields.Count);

        foreach (var index in indexes)
        {
            var names = index.Properties.Select(property => property.Name).ToList();

            names[0].ShouldBe(nameof(NoteRecord.OwnerId));
            names[^1].ShouldBe(nameof(NoteRecord.Id));
        }
    }

    [Fact]
    public void TheTable_LivesInTheNotesSchema()
    {
        Entity().GetTableName().ShouldBe("Notes");
        Entity().GetSchema().ShouldBe(AppDbContext.NotesSchema);
    }

    private static IEntityType Entity()
    {
        using var context = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=localhost;Database=probe;Username=none;Password=none")
            .Options);

        return context.Model.FindEntityType(typeof(NoteRecord))!;
    }
}

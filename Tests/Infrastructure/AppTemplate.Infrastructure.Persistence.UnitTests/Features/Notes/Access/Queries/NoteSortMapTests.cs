using System.Globalization;
using AppTemplate.Application.Core.Common.Collections;
using AppTemplate.Application.Core.Common.Policies;
using AppTemplate.Application.Features.Notes.Model.Dtos;
using AppTemplate.Application.Features.Notes.Model.Policies;
using AppTemplate.Infrastructure.Persistence.Common.Contexts;
using AppTemplate.Infrastructure.Persistence.Features.Notes.Access.Queries;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using Xunit;

namespace AppTemplate.Infrastructure.Persistence.UnitTests.Features.Notes.Access.Queries;

public sealed class NoteSortMapTests
{
    private static readonly NoteCollectionPolicy _policy = NoteCollectionPolicy.Instance;

    public static TheoryData<string> WhitelistedFieldNames()
    {
        var names = _policy.SortableFields.Select(field => field.Name).ToList();

        names.ShouldNotBeEmpty("an empty whitelist would run these theories zero times and pass.");

        return [.. names];
    }

    [Theory]
    [MemberData(nameof(WhitelistedFieldNames))]
    public void ApplyOrder_Ascending_EndsWithTheIdTiebreaker(string field)
    {
        using var context = AProbeContext();
        var sort = SortOrder.Parse($"{field}:asc", _policy).Value;

        string sql = NoteSortMap.ApplyOrder(context.Notes, sort).ToQueryString();

        sql.TrimEnd().ShouldEndWith($"ORDER BY n.\"{ColumnOf(field)}\", n.\"Id\"");
    }

    [Theory]
    [MemberData(nameof(WhitelistedFieldNames))]
    public void ApplyOrder_Descending_RunsTheTiebreakerDescendingToo(string field)
    {
        using var context = AProbeContext();
        var sort = SortOrder.Parse($"{field}:desc", _policy).Value;

        string sql = NoteSortMap.ApplyOrder(context.Notes, sort).ToQueryString();

        sql.TrimEnd().ShouldEndWith($"ORDER BY n.\"{ColumnOf(field)}\" DESC, n.\"Id\" DESC");
    }

    [Fact]
    public void ApplyOrder_AFieldNotOnTheWhitelist_Throws()
    {
        using var context = AProbeContext();
        var sort = SortOrder.Parse("bogus", new AllowsOnlyBogusFieldPolicy()).Value;

        Should.Throw<InvalidOperationException>(() => NoteSortMap.ApplyOrder(context.Notes, sort));
    }

    [Fact]
    public void ApplyKeyset_Ascending_IsAGreaterThanComparisonWithTheIdTiebreak()
    {
        using var context = AProbeContext();
        var term = SortOrder.Parse("title:asc", _policy).Value.Terms[0];

        string sql = NoteSortMap.ApplyKeyset(context.Notes, term, ACursor(term, "Groceries")).ToQueryString();

        sql.ShouldContain("WHERE n.\"Title\" > @cursor_Key OR (n.\"Title\" = @cursor_Key AND n.\"Id\" > @cursor_Id)");
    }

    [Fact]
    public void ApplyKeyset_Descending_IsALessThanComparisonWithTheIdTiebreak()
    {
        using var context = AProbeContext();
        var term = SortOrder.Parse("createdAt:desc", _policy).Value.Terms[0];

        string sql = NoteSortMap.ApplyKeyset(context.Notes, term, ACursor(term, DateTimeOffset.UtcNow.ToString("O")))
            .ToQueryString();

        sql.ShouldContain("WHERE n.\"CreatedAt\" < @key OR (n.\"CreatedAt\" = @key AND n.\"Id\" < @cursor_Id)");
    }

    [Fact]
    public void ApplyKeyset_AnUnparseableDateKey_Throws()
    {
        using var context = AProbeContext();
        var term = SortOrder.Parse("createdAt", _policy).Value.Terms[0];

        Should.Throw<InvalidOperationException>(
            () => NoteSortMap.ApplyKeyset(context.Notes, term, ACursor(term, "not-a-date")));
    }

    [Fact]
    public void KeyOf_Title_ReadsTheTitleVerbatim() =>
        NoteSortMap.KeyOf(ASummary(title: "Groceries"), NoteCollectionPolicy.TitleField).ShouldBe("Groceries");

    [Fact]
    public void KeyOf_CreatedAt_FormatsRoundTrippably()
    {
        var instant = new DateTimeOffset(2024, 1, 1, 0, 0, 0, 123, TimeSpan.Zero);

        string key = NoteSortMap.KeyOf(ASummary(createdAt: instant), NoteCollectionPolicy.CreatedAtField);

        DateTimeOffset.Parse(key, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind).ShouldBe(instant);
    }

    [Fact]
    public void KeyOf_AFieldNotOnTheWhitelist_Throws() =>
        Should.Throw<InvalidOperationException>(() => NoteSortMap.KeyOf(ASummary(), "bogus"));

    private static string ColumnOf(string field) => char.ToUpperInvariant(field[0]) + field[1..];

    private static NoteSummaryDto ASummary(string title = "Any", DateTimeOffset createdAt = default) =>
        new(Guid.CreateVersion7(), title, createdAt, null);

    private static Cursor ACursor(SortTerm term, string key) => Cursor.After(term, key, Guid.CreateVersion7());

    private static AppDbContext AProbeContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=localhost;Database=probe;Username=none;Password=none")
            .Options);

    private sealed class AllowsOnlyBogusFieldPolicy : ICollectionPolicy
    {
        public IReadOnlyList<SortableField> SortableFields { get; } = [SortableField.Keyset("bogus")];

        public string DefaultSort => "bogus";

        public int MaxSortTerms => 3;

        public int MaxPageSize => 100;

        public int DefaultPageSize => 20;
    }
}

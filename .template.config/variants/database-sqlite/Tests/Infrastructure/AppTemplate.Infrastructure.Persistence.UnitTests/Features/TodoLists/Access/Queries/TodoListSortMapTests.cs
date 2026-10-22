using System.Globalization;
using AppTemplate.Application.Core.Common.Collections;
using AppTemplate.Application.Core.Common.Policies;
using AppTemplate.Application.Features.TodoLists.Model.Dtos;
using AppTemplate.Application.Features.TodoLists.Model.Policies;
using AppTemplate.Infrastructure.Core.Common.Contexts;
using AppTemplate.Infrastructure.Persistence.Common.Contexts;
using AppTemplate.Infrastructure.Persistence.Features.TodoLists.Access.Queries;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using Xunit;

namespace AppTemplate.Infrastructure.Persistence.UnitTests.Features.TodoLists.Access.Queries;

public sealed class TodoListSortMapTests
{
    private static readonly TodoListCollectionPolicy _policy = TodoListCollectionPolicy.Instance;

    public static TheoryData<string> WhitelistedFieldNames()
    {
        var names = _policy.SortableFields.Select(field => field.Name).ToList();

        names.ShouldNotBeEmpty(
            $"{nameof(TodoListCollectionPolicy)}.{nameof(TodoListCollectionPolicy.Instance)} has no " +
            "SortableFields, so the theories driven by this member data would run zero cases and " +
            "pass for the wrong reason.");

        return [.. names];
    }

    [Theory]
    [MemberData(nameof(WhitelistedFieldNames))]
    public void ApplyOrder_EveryWhitelistedField_EndsTheOrderByWithTheIdTiebreaker(string field)
    {
        using var context = AProbeContext();
        var sort = SortOrder.Parse(field, _policy).Value;

        string sql = TodoListSortMap.ApplyOrder(context.TodoLists, sort).ToQueryString();

        sql.ShouldContain("ORDER BY");
        sql.TrimEnd().ShouldEndWith("\"Id\"");
    }

    [Theory]
    [MemberData(nameof(WhitelistedFieldNames))]
    public void ApplyOrder_Ascending_ProducesNoDescendingKeyword(string field)
    {
        using var context = AProbeContext();
        var sort = SortOrder.Parse($"{field}:asc", _policy).Value;

        string sql = TodoListSortMap.ApplyOrder(context.TodoLists, sort).ToQueryString();

        sql.ShouldContain($"ORDER BY \"t\".\"{ColumnOf(field)}\", \"t\".\"Id\"");
        sql.ShouldNotContain("DESC");
    }

    [Theory]
    [MemberData(nameof(WhitelistedFieldNames))]
    public void ApplyOrder_Descending_RunsTheTiebreakerDescendingToo(string field)
    {
        using var context = AProbeContext();
        var sort = SortOrder.Parse($"{field}:desc", _policy).Value;

        string sql = TodoListSortMap.ApplyOrder(context.TodoLists, sort).ToQueryString();

        sql.TrimEnd().ShouldEndWith($"ORDER BY \"t\".\"{ColumnOf(field)}\" DESC, \"t\".\"Id\" DESC");
    }

    [Fact]
    public void ApplyOrder_AMultiTermSort_RunsTheTiebreakerWithItsLastTerm()
    {
        using var context = AProbeContext();
        var sort = SortOrder.Parse("createdAt:asc,name:desc", _policy).Value;

        string sql = TodoListSortMap.ApplyOrder(context.TodoLists, sort).ToQueryString();

        sql.TrimEnd().ShouldEndWith("ORDER BY \"t\".\"CreatedAt\", \"t\".\"Name\" DESC, \"t\".\"Id\" DESC");
    }

    private static string ColumnOf(string field) =>
        char.ToUpperInvariant(field[0]) + field[1..];

    [Fact]
    public void ApplyOrder_AFieldNotOnTheWhitelist_Throws()
    {
        using var context = AProbeContext();
        var sort = SortOrder.Parse("bogus", new AllowsOnlyBogusFieldPolicy()).Value;

        Should.Throw<InvalidOperationException>(() => TodoListSortMap.ApplyOrder(context.TodoLists, sort));
    }

    [Fact]
    public void ApplyKeyset_Ascending_IsAGreaterThanComparisonWithTheIdTiebreak()
    {
        using var context = AProbeContext();
        var term = SortOrder.Parse("name:asc", _policy).Value.Terms[0];
        var cursor = ACursor(term, "Groceries");

        string sql = TodoListSortMap.ApplyKeyset(context.TodoLists, term, cursor).ToQueryString();

        sql.ShouldContain(
            "WHERE \"t\".\"Name\" > @cursor_Key OR (\"t\".\"Name\" = @cursor_Key AND \"t\".\"Id\" > @cursor_Id)");
    }

    [Fact]
    public void ApplyKeyset_Descending_IsALessThanComparisonWithTheIdTiebreak()
    {
        using var context = AProbeContext();
        var term = SortOrder.Parse("createdAt:desc", _policy).Value.Terms[0];
        var cursor = ACursor(term, DateTimeOffset.UtcNow.ToString("O"));

        string sql = TodoListSortMap.ApplyKeyset(context.TodoLists, term, cursor).ToQueryString();

        sql.ShouldContain("WHERE \"t\".\"CreatedAt\" < @key OR (\"t\".\"CreatedAt\" = @key AND \"t\".\"Id\" < @cursor_Id)");
    }

    [Fact]
    public void ApplyKeyset_AnUnparseableDateKey_Throws()
    {
        using var context = AProbeContext();
        var term = SortOrder.Parse("createdAt", _policy).Value.Terms[0];
        var cursor = ACursor(term, "not-a-date");

        Should.Throw<InvalidOperationException>(() => TodoListSortMap.ApplyKeyset(context.TodoLists, term, cursor));
    }

    [Fact]
    public void KeyOf_Name_ReadsTheNameVerbatim()
    {
        TodoListSortMap.KeyOf(ASummary(name: "Groceries"), TodoListCollectionPolicy.NameField)
            .ShouldBe("Groceries");
    }

    [Fact]
    public void KeyOf_CreatedAt_FormatsRoundTrippably()
    {
        var instant = new DateTimeOffset(2024, 1, 1, 0, 0, 0, 123, TimeSpan.Zero);

        string key = TodoListSortMap.KeyOf(ASummary(createdAt: instant), TodoListCollectionPolicy.CreatedAtField);

        DateTimeOffset.Parse(key, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)
            .ShouldBe(instant);
    }

    [Fact]
    public void KeyOf_AFieldNotOnTheWhitelist_Throws() =>
        Should.Throw<InvalidOperationException>(() => TodoListSortMap.KeyOf(ASummary(), "bogus"));

    private static TodoListSummaryDto ASummary(string name = "Any", DateTimeOffset createdAt = default) =>
        new(Guid.CreateVersion7(), name, ItemCount: 0, CompletedItemCount: 0, createdAt);

    private static Cursor ACursor(SortTerm term, string key) =>
        Cursor.After(term, key, Guid.CreateVersion7());

    private static AppDbContext AProbeContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseDatabase(DatabaseProvider.DesignTimeConnectionString)
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

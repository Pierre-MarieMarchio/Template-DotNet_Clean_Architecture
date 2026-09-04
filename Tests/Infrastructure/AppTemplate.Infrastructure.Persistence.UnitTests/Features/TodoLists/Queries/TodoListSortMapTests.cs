using System.Globalization;
using AppTemplate.Application.Common.Collections;
using AppTemplate.Application.Common.Policies;
using AppTemplate.Application.Features.TodoLists.Dtos;
using AppTemplate.Application.Features.TodoLists.Policies;
using AppTemplate.Infrastructure.Persistence.Common.Contexts;
using AppTemplate.Infrastructure.Persistence.Features.TodoLists.Queries;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using Xunit;

namespace AppTemplate.Infrastructure.Persistence.UnitTests.Features.TodoLists.Queries;

/// <summary>
/// SQL-shape assertions against <c>ToQueryString()</c>. No database is contacted: the context is
/// configured against PostgreSQL only so EF has a provider and a model to translate against — the
/// same arrangement <see cref="Common.Saving.EfUnitOfWorkTests"/> uses.
/// <para>
/// Every sort here is built through the real <see cref="SortOrder.Parse"/> and every cursor through
/// the real <see cref="Cursor.After"/>, never by hand-constructing internals, so these tests
/// exercise exactly what a request would produce.
/// </para>
/// </summary>
public sealed class TodoListSortMapTests
{
    private static readonly TodoListCollectionPolicy _policy = TodoListCollectionPolicy.Instance;

    #region ApplyOrder

    /// <summary>
    /// Driven by the policy's own whitelist rather than a hard-coded field list, so a newly
    /// whitelisted field is covered by these three theories automatically instead of silently
    /// untested.
    /// </summary>
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

        sql.ShouldContain($"ORDER BY t.\"{ColumnOf(field)}\", t.\"Id\"");
        sql.ShouldNotContain("DESC");
    }

    /// <summary>
    /// The tiebreaker must run the <em>same way</em> as the term it breaks ties for, and this
    /// assertion is deliberately exact rather than a <c>ShouldContain</c> prefix: a prefix match is
    /// satisfied by both <c>"Id"</c> and <c>"Id" DESC</c>, so it would pass whichever direction the
    /// tiebreaker took and pin nothing. A descending order with an ascending tiebreaker orders tied
    /// rows one way while <see cref="TodoListSortMap.ApplyKeyset"/> walks them the other, and every
    /// row tied on the sort key past the cursor is then silently skipped.
    /// </summary>
    [Theory]
    [MemberData(nameof(WhitelistedFieldNames))]
    public void ApplyOrder_Descending_RunsTheTiebreakerDescendingToo(string field)
    {
        using var context = AProbeContext();
        var sort = SortOrder.Parse($"{field}:desc", _policy).Value;

        string sql = TodoListSortMap.ApplyOrder(context.TodoLists, sort).ToQueryString();

        sql.TrimEnd().ShouldEndWith($"ORDER BY t.\"{ColumnOf(field)}\" DESC, t.\"Id\" DESC");
    }

    /// <summary>
    /// In a multi-term sort the tiebreaker follows the <em>last</em> term, so the order it continues
    /// is the one immediately before it.
    /// </summary>
    [Fact]
    public void ApplyOrder_AMultiTermSort_RunsTheTiebreakerWithItsLastTerm()
    {
        using var context = AProbeContext();
        var sort = SortOrder.Parse("createdAt:asc,name:desc", _policy).Value;

        string sql = TodoListSortMap.ApplyOrder(context.TodoLists, sort).ToQueryString();

        sql.TrimEnd().ShouldEndWith("ORDER BY t.\"CreatedAt\", t.\"Name\" DESC, t.\"Id\" DESC");
    }

    /// <summary>
    /// The whitelist's field names are camelCase and the columns they map to are the same name
    /// PascalCased — the same convention <see cref="TodoListSortMap"/> itself relies on for every
    /// field it knows about. Kept local to the two exact-match theories above rather than promoted to
    /// production code, since nothing there needs a field name turned into a column name in general.
    /// </summary>
    private static string ColumnOf(string field) =>
        char.ToUpperInvariant(field[0]) + field[1..];

    /// <summary>
    /// <see cref="TodoListSortMap"/> only recognises <see cref="TodoListCollectionPolicy"/>'s own
    /// field constants. <see cref="SortOrder.Parse"/> under the real policy would never let an
    /// unknown name through, so the only honest way to reach the default arm is a policy double that
    /// whitelists a name this map does not — without weakening any type along the way.
    /// </summary>
    [Fact]
    public void ApplyOrder_AFieldNotOnTheWhitelist_Throws()
    {
        using var context = AProbeContext();
        var sort = SortOrder.Parse("bogus", new AllowsOnlyBogusFieldPolicy()).Value;

        Should.Throw<InvalidOperationException>(() => TodoListSortMap.ApplyOrder(context.TodoLists, sort));
    }

    #endregion

    #region ApplyKeyset

    [Fact]
    public void ApplyKeyset_Ascending_IsAGreaterThanComparisonWithTheIdTiebreak()
    {
        using var context = AProbeContext();
        var term = SortOrder.Parse("name:asc", _policy).Value.Terms[0];
        var cursor = ACursor(term, "Groceries");

        string sql = TodoListSortMap.ApplyKeyset(context.TodoLists, term, cursor).ToQueryString();

        sql.ShouldContain(
            "WHERE t.\"Name\" > @cursor_Key OR (t.\"Name\" = @cursor_Key AND t.\"Id\" > @cursor_Id)");
    }

    [Fact]
    public void ApplyKeyset_Descending_IsALessThanComparisonWithTheIdTiebreak()
    {
        using var context = AProbeContext();
        var term = SortOrder.Parse("createdAt:desc", _policy).Value.Terms[0];
        var cursor = ACursor(term, DateTimeOffset.UtcNow.ToString("O"));

        string sql = TodoListSortMap.ApplyKeyset(context.TodoLists, term, cursor).ToQueryString();

        sql.ShouldContain("WHERE t.\"CreatedAt\" < @key OR (t.\"CreatedAt\" = @key AND t.\"Id\" < @cursor_Id)");
    }

    /// <summary>
    /// A createdAt key that does not parse as a date only reaches here if
    /// <c>CursorKeys.Validate</c> was bypassed — this proves that failure mode is loud
    /// rather than a wrong or silently-empty result.
    /// </summary>
    [Fact]
    public void ApplyKeyset_AnUnparseableDateKey_Throws()
    {
        using var context = AProbeContext();
        var term = SortOrder.Parse("createdAt", _policy).Value.Terms[0];
        var cursor = ACursor(term, "not-a-date");

        Should.Throw<InvalidOperationException>(() => TodoListSortMap.ApplyKeyset(context.TodoLists, term, cursor));
    }

    #endregion

    #region KeyOf

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

        // Round-trip exactly, sub-second included: a key that lost precision would place the cursor
        // before rows the caller has already been served, and serve them a second time.
        DateTimeOffset.Parse(key, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)
            .ShouldBe(instant);
    }

    [Fact]
    public void KeyOf_AFieldNotOnTheWhitelist_Throws()
    {
        Should.Throw<InvalidOperationException>(() => TodoListSortMap.KeyOf(ASummary(), "bogus"));
    }

    private static TodoListSummaryDto ASummary(string name = "Any", DateTimeOffset createdAt = default) =>
        new(Guid.CreateVersion7(), name, ItemCount: 0, CompletedItemCount: 0, createdAt);

    #endregion

    /// <remarks>
    /// Built through the public <see cref="Cursor.After"/> — the same call the read side makes to
    /// mint a next-page cursor. Decoding is not exercised here: this project has no access to
    /// <c>Cursor.Decode</c> by design, and what a decoded cursor accepts or refuses is covered where
    /// that code lives, in the application layer's own tests.
    /// </remarks>
    private static Cursor ACursor(SortTerm term, string key) =>
        Cursor.After(term, key, Guid.CreateVersion7());

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

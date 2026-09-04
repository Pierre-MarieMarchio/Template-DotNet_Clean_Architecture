using System.Globalization;
using AppTemplate.Application.Common.Collections;
using AppTemplate.Application.Features.Files.Dtos;
using AppTemplate.Application.Features.Files.Policies;
using AppTemplate.Domain.Features.Files.ValueObjects;
using AppTemplate.Infrastructure.Persistence.Common.Contexts;
using AppTemplate.Infrastructure.Persistence.Features.Files.Queries;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using Xunit;

namespace AppTemplate.Infrastructure.Persistence.UnitTests.Features.Files.Queries;

/// <summary>
/// SQL-shape assertions against <c>ToQueryString()</c>. No database is contacted: the context is
/// configured against PostgreSQL only so EF has a provider and a model to translate against.
/// <para>
/// Every sort here is built through the real <see cref="SortOrder.Parse"/> and every cursor through the
/// real <see cref="Cursor.After"/>, never by hand-constructing internals, so these tests exercise
/// exactly what a request would produce.
/// </para>
/// </summary>
public sealed class StoredFileSortMapTests
{
    private static readonly StoredFileCollectionPolicy _policy = StoredFileCollectionPolicy.Instance;

    #region ApplyOrder

    /// <summary>
    /// Driven by the policy's own whitelist rather than a hard-coded field list, so a newly whitelisted
    /// field is covered by these theories automatically instead of silently untested.
    /// </summary>
    public static TheoryData<string> WhitelistedFieldNames()
    {
        var names = _policy.SortableFields.Select(field => field.Name).ToList();

        names.ShouldNotBeEmpty(
            $"{nameof(StoredFileCollectionPolicy)}.{nameof(StoredFileCollectionPolicy.Instance)} has no " +
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

        string sql = StoredFileSortMap.ApplyOrder(context.StoredFiles, sort).ToQueryString();

        sql.ShouldContain("ORDER BY");
        sql.TrimEnd().ShouldEndWith("\"Id\"");
    }

    [Theory]
    [MemberData(nameof(WhitelistedFieldNames))]
    public void ApplyOrder_Ascending_ProducesNoDescendingKeyword(string field)
    {
        using var context = AProbeContext();
        var sort = SortOrder.Parse($"{field}:asc", _policy).Value;

        string sql = StoredFileSortMap.ApplyOrder(context.StoredFiles, sort).ToQueryString();

        sql.ShouldContain($"ORDER BY s.\"{ColumnOf(field)}\", s.\"Id\"");
        sql.ShouldNotContain("DESC");
    }

    /// <summary>
    /// The tiebreaker must run the <em>same way</em> as the term it breaks ties for, and this assertion
    /// is deliberately exact rather than a <c>ShouldContain</c> prefix: a prefix match is satisfied by
    /// both <c>"Id"</c> and <c>"Id" DESC</c>, so it would pass whichever direction the tiebreaker took
    /// and pin nothing. A descending order with an ascending tiebreaker orders tied rows one way while
    /// <see cref="StoredFileSortMap.ApplyKeyset"/> walks them the other, and every row tied on the sort
    /// key past the cursor is then silently skipped. It is also what the feature's own default sort —
    /// <c>registeredAt:desc</c> — takes every time nobody asks for anything else.
    /// </summary>
    [Theory]
    [MemberData(nameof(WhitelistedFieldNames))]
    public void ApplyOrder_Descending_RunsTheTiebreakerDescendingToo(string field)
    {
        using var context = AProbeContext();
        var sort = SortOrder.Parse($"{field}:desc", _policy).Value;

        string sql = StoredFileSortMap.ApplyOrder(context.StoredFiles, sort).ToQueryString();

        sql.TrimEnd().ShouldEndWith($"ORDER BY s.\"{ColumnOf(field)}\" DESC, s.\"Id\" DESC");
    }

    /// <summary>
    /// The policy's own <see cref="StoredFileCollectionPolicy.DefaultSort"/>, parsed by the same code a
    /// caller's string goes through, so the order served when nobody asks for one is pinned too.
    /// </summary>
    [Fact]
    public void ApplyOrder_TheFeaturesDefaultSort_IsNewestFirst()
    {
        using var context = AProbeContext();
        var sort = SortOrder.Parse(_policy.DefaultSort, _policy).Value;

        string sql = StoredFileSortMap.ApplyOrder(context.StoredFiles, sort).ToQueryString();

        sql.TrimEnd().ShouldEndWith("ORDER BY s.\"RegisteredAt\" DESC, s.\"Id\" DESC");
    }

    /// <summary>
    /// In a multi-term sort the tiebreaker follows the <em>last</em> term, so the order it continues is
    /// the one immediately before it.
    /// </summary>
    [Fact]
    public void ApplyOrder_AMultiTermSort_RunsTheTiebreakerWithItsLastTerm()
    {
        using var context = AProbeContext();
        var sort = SortOrder.Parse("registeredAt:asc,name:desc", _policy).Value;

        string sql = StoredFileSortMap.ApplyOrder(context.StoredFiles, sort).ToQueryString();

        sql.TrimEnd().ShouldEndWith("ORDER BY s.\"RegisteredAt\", s.\"Name\" DESC, s.\"Id\" DESC");
    }

    /// <summary>
    /// The whitelist's field names are camelCase and the columns they map to are the same name
    /// PascalCased — the convention <see cref="StoredFileSortMap"/> itself relies on for every field it
    /// knows about.
    /// </summary>
    private static string ColumnOf(string field) =>
        char.ToUpperInvariant(field[0]) + field[1..];

    /// <summary>
    /// <see cref="StoredFileSortMap"/> only recognises <see cref="StoredFileCollectionPolicy"/>'s own
    /// field constants. <see cref="SortOrder.Parse"/> under the real policy would never let an unknown
    /// name through, so the only honest way to reach the default arm is a policy double that whitelists
    /// a name this map does not.
    /// </summary>
    [Fact]
    public void ApplyOrder_AFieldNotOnTheWhitelist_Throws()
    {
        using var context = AProbeContext();
        var sort = SortOrder.Parse("bogus", new AllowsOnlyBogusFieldPolicy()).Value;

        Should.Throw<InvalidOperationException>(() => StoredFileSortMap.ApplyOrder(context.StoredFiles, sort));
    }

    #endregion

    #region ApplyKeyset

    [Fact]
    public void ApplyKeyset_Ascending_IsAGreaterThanComparisonWithTheIdTiebreak()
    {
        using var context = AProbeContext();
        var term = SortOrder.Parse("name:asc", _policy).Value.Terms[0];
        var cursor = ACursor(term, "quarterly-report.pdf");

        string sql = StoredFileSortMap.ApplyKeyset(context.StoredFiles, term, cursor).ToQueryString();

        sql.ShouldContain(
            "WHERE s.\"Name\" > @cursor_Key OR (s.\"Name\" = @cursor_Key AND s.\"Id\" > @cursor_Id)");
    }

    [Fact]
    public void ApplyKeyset_Descending_IsALessThanComparisonWithTheIdTiebreak()
    {
        using var context = AProbeContext();
        var term = SortOrder.Parse("registeredAt:desc", _policy).Value.Terms[0];
        var cursor = ACursor(term, DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));

        string sql = StoredFileSortMap.ApplyKeyset(context.StoredFiles, term, cursor).ToQueryString();

        sql.ShouldContain("WHERE s.\"RegisteredAt\" < @key OR (s.\"RegisteredAt\" = @key AND s.\"Id\" < @cursor_Id)");
    }

    /// <summary>
    /// <c>availableAt</c> is on the whitelist as <see cref="SortableField.OffsetOnly"/> because its
    /// column is nullable, and a keyset comparison against <c>NULL</c> is neither true nor false — the
    /// row a cursor was minted from would be skipped rather than resumed from. The binder refuses a
    /// cursor over it long before this point, so reaching here is a defect in the template and has to
    /// stay loud rather than quietly serving the wrong page.
    /// </summary>
    [Fact]
    public void ApplyKeyset_AnOffsetOnlyField_Throws()
    {
        using var context = AProbeContext();
        var term = SortOrder.Parse(StoredFileCollectionPolicy.AvailableAtField, _policy).Value.Terms[0];
        var cursor = ACursor(term, DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));

        Should.Throw<InvalidOperationException>(
            () => StoredFileSortMap.ApplyKeyset(context.StoredFiles, term, cursor));
    }

    /// <summary>
    /// A registeredAt key that does not parse as a date only reaches here if
    /// <c>CursorKeys.Validate</c> was bypassed — this proves that failure mode is loud
    /// rather than a wrong or silently-empty result.
    /// </summary>
    [Fact]
    public void ApplyKeyset_AnUnparseableDateKey_Throws()
    {
        using var context = AProbeContext();
        var term = SortOrder.Parse("registeredAt", _policy).Value.Terms[0];
        var cursor = ACursor(term, "not-a-date");

        Should.Throw<InvalidOperationException>(
            () => StoredFileSortMap.ApplyKeyset(context.StoredFiles, term, cursor));
    }

    #endregion

    #region KeyOf

    [Fact]
    public void KeyOf_Name_ReadsTheNameVerbatim()
    {
        StoredFileSortMap.KeyOf(ADto(name: "quarterly-report.pdf"), StoredFileCollectionPolicy.NameField)
            .ShouldBe("quarterly-report.pdf");
    }

    [Fact]
    public void KeyOf_RegisteredAt_FormatsRoundTrippably()
    {
        var instant = new DateTimeOffset(2026, 1, 1, 0, 0, 0, 123, TimeSpan.Zero);

        string key = StoredFileSortMap.KeyOf(ADto(registeredAt: instant), StoredFileCollectionPolicy.RegisteredAtField);

        // Round-trip exactly, sub-second included: a key that lost precision would place the cursor
        // before rows the caller has already been served, and serve them a second time.
        DateTimeOffset.Parse(key, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)
            .ShouldBe(instant);
    }

    [Fact]
    public void KeyOf_AFieldNotOnTheWhitelist_Throws()
    {
        Should.Throw<InvalidOperationException>(() => StoredFileSortMap.KeyOf(ADto(), "bogus"));
    }

    private static StoredFileDto ADto(string name = "any.bin", DateTimeOffset registeredAt = default) =>
        new(
            Guid.CreateVersion7(),
            name,
            "application/octet-stream",
            SizeInBytes: 1,
            new string('a', Sha256Checksum.Length),
            StoredFileState.Available,
            registeredAt,
            AvailableAt: null);

    #endregion

    /// <remarks>
    /// Built through the public <see cref="Cursor.After"/> — the same call the read side makes to mint a
    /// next-page cursor. Decoding is not exercised here: this project has no access to
    /// <c>Cursor.Decode</c> by design, and what a decoded cursor accepts or refuses is covered where that
    /// code lives, in the application layer's own tests.
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

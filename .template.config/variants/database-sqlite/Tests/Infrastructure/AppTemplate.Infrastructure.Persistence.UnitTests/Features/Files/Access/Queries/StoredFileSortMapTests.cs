using System.Globalization;
using AppTemplate.Application.Core.Common.Collections;
using AppTemplate.Application.Core.Common.Policies;
using AppTemplate.Application.Features.Files.Model.Dtos;
using AppTemplate.Application.Features.Files.Model.Policies;
using AppTemplate.Domain.Features.Files.Model.ValueObjects;
using AppTemplate.Infrastructure.Core.Common.Contexts;
using AppTemplate.Infrastructure.Persistence.Common.Contexts;
using AppTemplate.Infrastructure.Persistence.Features.Files.Access.Queries;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using Xunit;

namespace AppTemplate.Infrastructure.Persistence.UnitTests.Features.Files.Access.Queries;

public sealed class StoredFileSortMapTests
{
    private static readonly StoredFileCollectionPolicy _policy = StoredFileCollectionPolicy.Instance;

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

        sql.ShouldContain($"ORDER BY \"f\".\"{ColumnOf(field)}\", \"f\".\"Id\"");
        sql.ShouldNotContain("DESC");
    }

    [Theory]
    [MemberData(nameof(WhitelistedFieldNames))]
    public void ApplyOrder_Descending_RunsTheTiebreakerDescendingToo(string field)
    {
        using var context = AProbeContext();
        var sort = SortOrder.Parse($"{field}:desc", _policy).Value;

        string sql = StoredFileSortMap.ApplyOrder(context.StoredFiles, sort).ToQueryString();

        sql.TrimEnd().ShouldEndWith($"ORDER BY \"f\".\"{ColumnOf(field)}\" DESC, \"f\".\"Id\" DESC");
    }

    [Fact]
    public void ApplyOrder_TheFeaturesDefaultSort_IsNewestFirst()
    {
        using var context = AProbeContext();
        var sort = SortOrder.Parse(_policy.DefaultSort, _policy).Value;

        string sql = StoredFileSortMap.ApplyOrder(context.StoredFiles, sort).ToQueryString();

        sql.TrimEnd().ShouldEndWith("ORDER BY \"f\".\"RegisteredAt\" DESC, \"f\".\"Id\" DESC");
    }

    [Fact]
    public void ApplyOrder_AMultiTermSort_RunsTheTiebreakerWithItsLastTerm()
    {
        using var context = AProbeContext();
        var sort = SortOrder.Parse("registeredAt:asc,name:desc", _policy).Value;

        string sql = StoredFileSortMap.ApplyOrder(context.StoredFiles, sort).ToQueryString();

        sql.TrimEnd().ShouldEndWith("ORDER BY \"f\".\"RegisteredAt\", \"f\".\"Name\" DESC, \"f\".\"Id\" DESC");
    }

    private static string ColumnOf(string field) =>
        char.ToUpperInvariant(field[0]) + field[1..];

    [Fact]
    public void ApplyOrder_AFieldNotOnTheWhitelist_Throws()
    {
        using var context = AProbeContext();
        var sort = SortOrder.Parse("bogus", new AllowsOnlyBogusFieldPolicy()).Value;

        Should.Throw<InvalidOperationException>(() => StoredFileSortMap.ApplyOrder(context.StoredFiles, sort));
    }

    [Fact]
    public void ApplyKeyset_Ascending_IsAGreaterThanComparisonWithTheIdTiebreak()
    {
        using var context = AProbeContext();
        var term = SortOrder.Parse("name:asc", _policy).Value.Terms[0];
        var cursor = ACursor(term, "quarterly-report.pdf");

        string sql = StoredFileSortMap.ApplyKeyset(context.StoredFiles, term, cursor).ToQueryString();

        sql.ShouldContain(
            "WHERE \"f\".\"Name\" > @cursor_Key OR (\"f\".\"Name\" = @cursor_Key AND \"f\".\"Id\" > @cursor_Id)");
    }

    [Fact]
    public void ApplyKeyset_Descending_IsALessThanComparisonWithTheIdTiebreak()
    {
        using var context = AProbeContext();
        var term = SortOrder.Parse("registeredAt:desc", _policy).Value.Terms[0];
        var cursor = ACursor(term, DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));

        string sql = StoredFileSortMap.ApplyKeyset(context.StoredFiles, term, cursor).ToQueryString();

        sql.ShouldContain("WHERE \"f\".\"RegisteredAt\" < @key OR (\"f\".\"RegisteredAt\" = @key AND \"f\".\"Id\" < @cursor_Id)");
    }

    [Fact]
    public void ApplyKeyset_AnOffsetOnlyField_Throws()
    {
        using var context = AProbeContext();
        var term = SortOrder.Parse(StoredFileCollectionPolicy.AvailableAtField, _policy).Value.Terms[0];
        var cursor = ACursor(term, DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));

        Should.Throw<InvalidOperationException>(
            () => StoredFileSortMap.ApplyKeyset(context.StoredFiles, term, cursor));
    }

    [Fact]
    public void ApplyKeyset_AnUnparseableDateKey_Throws()
    {
        using var context = AProbeContext();
        var term = SortOrder.Parse("registeredAt", _policy).Value.Terms[0];
        var cursor = ACursor(term, "not-a-date");

        Should.Throw<InvalidOperationException>(
            () => StoredFileSortMap.ApplyKeyset(context.StoredFiles, term, cursor));
    }

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

        DateTimeOffset.Parse(key, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)
            .ShouldBe(instant);
    }

    [Fact]
    public void KeyOf_AFieldNotOnTheWhitelist_Throws() =>
        Should.Throw<InvalidOperationException>(() => StoredFileSortMap.KeyOf(ADto(), "bogus"));

    private static StoredFileDto ADto(string name = "any.bin", DateTimeOffset registeredAt = default) =>
        new(
            Guid.CreateVersion7(),
            "document",
            name,
            "application/octet-stream",
            SizeInBytes: 1,
            new string('a', Sha256Checksum.Length),
            StoredFileState.Available,
            registeredAt,
            AvailableAt: null,
            Tags: []);

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

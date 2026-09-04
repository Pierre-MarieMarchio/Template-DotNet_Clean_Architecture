using AppTemplate.Application.Common.Collections;
using AppTemplate.Application.Features.Files.Policies;
using Shouldly;
using Xunit;

namespace AppTemplate.Application.UnitTests.Features.Files.Policies;

/// <summary>
/// The architecture suite already asserts that every policy is internally consistent — its default
/// sort parses against its own whitelist, its page sizes agree. What it cannot assert is the
/// promise a specific name carries, which is what these two check.
/// </summary>
public sealed class StoredFileCollectionPolicyTests
{
    /// <summary>
    /// A keyset comparison against <c>NULL</c> is neither true nor false, so a nullable column may
    /// only be sorted on in offset mode. <c>availableAt</c> is null for every file that was never
    /// confirmed, which is precisely the population a user filters for.
    /// </summary>
    [Fact]
    public void TheNullableColumn_IsOffsetOnly() =>
        StoredFileCollectionPolicy.Instance.SortableFields
            .Single(field => field.Name == StoredFileCollectionPolicy.AvailableAtField)
            .SupportsKeyset.ShouldBeFalse();

    [Fact]
    public void TheNonNullableColumns_SupportKeyset() =>
        StoredFileCollectionPolicy.Instance.SortableFields
            .Where(field => field.Name is StoredFileCollectionPolicy.NameField
                or StoredFileCollectionPolicy.RegisteredAtField)
            .ShouldAllBe(field => field.SupportsKeyset);

    /// <summary>
    /// Every name here is a promise that the persistence configuration carries a composite index for
    /// it. Growing the list is cheap and keeping that promise is not, so the count is stated once
    /// where a reviewer will see it move.
    /// </summary>
    [Fact]
    public void TheWhitelist_IsThreeFieldsWide() =>
        StoredFileCollectionPolicy.Instance.SortableFields.Count.ShouldBe(3);

    [Fact]
    public void TheDefaultSort_PutsTheNewestFirst()
    {
        var parsed = SortOrder.Parse(
            StoredFileCollectionPolicy.Instance.DefaultSort,
            StoredFileCollectionPolicy.Instance);

        parsed.IsSuccess.ShouldBeTrue();
        parsed.Value.Terms.Count.ShouldBe(1);
        parsed.Value.Terms[0].Field.ShouldBe(StoredFileCollectionPolicy.RegisteredAtField);
        parsed.Value.Terms[0].Direction.ShouldBe(
            AppTemplate.Application.Common.Collections.SortDirection.Descending);
    }
}

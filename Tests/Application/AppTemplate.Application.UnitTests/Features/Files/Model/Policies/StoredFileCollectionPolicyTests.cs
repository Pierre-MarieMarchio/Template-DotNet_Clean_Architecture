using AppTemplate.Application.Core.Common.Collections;
using AppTemplate.Application.Features.Files.Model.Policies;
using Shouldly;
using Xunit;

namespace AppTemplate.Application.UnitTests.Features.Files.Model.Policies;

public sealed class StoredFileCollectionPolicyTests
{
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
            AppTemplate.Application.Core.Common.Collections.SortDirection.Descending);
    }
}

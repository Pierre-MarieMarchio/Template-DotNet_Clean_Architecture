using AppTemplate.Application.Core.Common.Collections;
using Shouldly;
using Xunit;

namespace AppTemplate.Application.Core.UnitTests.Common.Collections;

public sealed class CollectionOrderTests
{
    private readonly FakeCollectionPolicy _policy = new();

    [Theory]
    [InlineData("name:asc")]
    [InlineData("createdAt:desc")]
    public void CursorPaging_AcceptsAKeysetCapableField(string sort)
    {
        var result = CollectionOrder.Parse("cursor", sort, _policy);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Mode.ShouldBe(PagingMode.Cursor);
    }

    [Fact]
    public void CursorPaging_RefusesAnOffsetOnlyField_OnTheFirstPageToo()
    {
        var result = CollectionOrder.Parse("cursor", "lastModifiedAt:asc", _policy);

        result.IsFailure.ShouldBeTrue(
            "an offset-only field must be refused before a page is served, not when the cursor for "
            + "the next one is minted");
        result.Error!.Code.ShouldBe("cursor.invalid");
        result.Error.Message.ShouldContain("lastModifiedAt");
        result.Error.Message.ShouldContain("name", Case.Sensitive);
    }

    [Fact]
    public void OffsetPaging_AcceptsAnOffsetOnlyField()
    {
        CollectionOrder.Parse("offset", "lastModifiedAt:asc", _policy).IsSuccess.ShouldBeTrue();
        CollectionOrder.Parse(null, "lastModifiedAt:asc", _policy).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void CursorPaging_RefusesMoreThanOneSortTerm()
    {
        var result = CollectionOrder.Parse("cursor", "name:asc,createdAt:desc", _policy);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Code.ShouldBe("cursor.invalid");
    }

    [Fact]
    public void CursorPaging_JudgesThePolicysOwnDefaultSortByTheSameRule()
    {
        var offsetOnlyDefault = new FakeCollectionPolicy { DefaultSort = "lastModifiedAt:desc" };

        var result = CollectionOrder.Parse("cursor", sort: null, offsetOnlyDefault);

        result.IsFailure.ShouldBeTrue(
            "a default the feature declares is not exempt from the rule a caller's input meets");
        result.Error!.Code.ShouldBe("cursor.invalid");
    }

    [Fact]
    public void AnUnknownPagingMode_IsRefusedBeforeTheSortIsRead()
    {
        var result = CollectionOrder.Parse("sideways", "nonsense", _policy);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Code.ShouldBe("paging.invalid");
    }
}

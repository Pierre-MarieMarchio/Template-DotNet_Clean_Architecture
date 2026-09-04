using AppTemplate.Application.Common.Collections;
using Shouldly;
using Xunit;

namespace AppTemplate.Application.UnitTests.Common.Collections;

/// <summary>
/// <see cref="CollectionOrder.Parse"/> is the only place that sees the paging mode and the sort
/// order at once, so every rule about the two together has to live there or nowhere. The rules that
/// matter are the ones a caller could otherwise clear on page 1 and break on page 2, because the
/// read side's only recourse at that point is to throw — a 500 for a rule the caller broke.
/// </summary>
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

    /// <summary>
    /// The regression this file was added for. Nothing sent a cursor here, so the check that
    /// <c>Cursor.Decode</c> performs never ran, and minting <c>nextCursor</c> over a nullable column
    /// reached a translator whose only answer is to throw.
    /// </summary>
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

    /// <summary>
    /// The same field is legitimate under offset paging: it is nullable, which keyset resumption
    /// cannot survive, and ordinary ordering can.
    /// </summary>
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

    /// <summary>
    /// A policy whose default sort is offset-only would refuse every cursor request that named no
    /// sort at all. The default is parsed through the same body a caller's input takes, so the check
    /// has to be as true of it as of anything else.
    /// </summary>
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

using AppTemplate.Application.Common.Results;
using Shouldly;
using Xunit;

namespace AppTemplate.Application.UnitTests.Common.Results;

public sealed class PagedResultTests
{
    #region Offset mode

    /// <summary>
    /// The page count is a ceiling, not a truncation: 11 rows in pages of 10 is two pages,
    /// and an integer division would lose the second one.
    /// </summary>
    [Theory]
    [InlineData(0, 10, 0)]
    [InlineData(1, 10, 1)]
    [InlineData(9, 10, 1)]
    [InlineData(10, 10, 1)]
    [InlineData(11, 10, 2)]
    [InlineData(25, 10, 3)]
    [InlineData(100, 1, 100)]
    public void TotalPages_RoundsUp(int totalCount, int pageSize, int expectedTotalPages) =>
        AnOffsetPage(1, pageSize, totalCount).TotalPages.ShouldBe(expectedTotalPages);

    /// <summary>
    /// A non-positive page size is guarded rather than divided by, so a malformed page
    /// cannot turn a read into a <c>DivideByZeroException</c> — or into infinity.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public void TotalPages_IsZero_WhenThePageSizeIsNotPositive(int pageSize) =>
        AnOffsetPage(1, pageSize, 100).TotalPages.ShouldBe(0);

    [Fact]
    public void HasNextPage_IsTrue_WhileFurtherPagesExist()
    {
        AnOffsetPage(1, 10, 25).HasNextPage.ShouldBeTrue();
        AnOffsetPage(2, 10, 25).HasNextPage.ShouldBeTrue();
    }

    [Fact]
    public void HasNextPage_IsFalse_OnTheLastPage() => AnOffsetPage(3, 10, 25).HasNextPage.ShouldBeFalse();

    [Fact]
    public void HasNextPage_IsFalse_BeyondTheLastPage() => AnOffsetPage(4, 10, 25).HasNextPage.ShouldBeFalse();

    [Fact]
    public void HasNextPage_IsFalse_WhenThereAreNoRowsAtAll() =>
        AnOffsetPage(1, 10, 0).HasNextPage.ShouldBeFalse();

    [Fact]
    public void HasNextPage_IsFalse_WhenThePageSizeIsNotPositive() =>
        AnOffsetPage(1, 0, 100).HasNextPage.ShouldBeFalse();

    [Fact]
    public void AnOffsetPage_CarriesNoCursor() => AnOffsetPage(1, 10, 25).NextCursor.ShouldBeNull();

    #endregion

    #region Cursor mode

    /// <summary>
    /// Counting the whole match set is the cost keyset paging exists to avoid, so a keyset page
    /// answers no total at all rather than a wrong one.
    /// </summary>
    [Fact]
    public void AKeysetPage_CarriesNoTotalCountOrPageNumber()
    {
        var page = AKeysetPage(10, "next-cursor-token");

        page.TotalCount.ShouldBeNull();
        page.Page.ShouldBeNull();
        page.TotalPages.ShouldBeNull();
    }

    [Fact]
    public void AKeysetPage_HasNextPage_WhenACursorWasMinted() =>
        AKeysetPage(10, "next-cursor-token").HasNextPage.ShouldBeTrue();

    [Fact]
    public void AKeysetPage_HasNoNextPage_WhenNoCursorWasMinted() =>
        AKeysetPage(10, null).HasNextPage.ShouldBeFalse();

    #endregion

    private static PagedResult<string> AnOffsetPage(int page, int pageSize, int totalCount) =>
        PagedResult.Offset<string>([], page, pageSize, totalCount);

    private static PagedResult<string> AKeysetPage(int pageSize, string? nextCursor) =>
        PagedResult.Keyset<string>([], pageSize, nextCursor);
}

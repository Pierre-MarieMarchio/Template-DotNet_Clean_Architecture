using AppTemplate.Application.Common;
using Shouldly;
using Xunit;

namespace AppTemplate.Application.UnitTests.Common;

public sealed class PagedResultTests
{
    private static PagedResult<string> APage(int page, int pageSize, int totalCount) =>
        new([], page, pageSize, totalCount);

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
        APage(1, pageSize, totalCount).TotalPages.ShouldBe(expectedTotalPages);

    /// <summary>
    /// A non-positive page size is guarded rather than divided by, so a malformed page
    /// cannot turn a read into a <c>DivideByZeroException</c> — or into infinity.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public void TotalPages_IsZero_WhenThePageSizeIsNotPositive(int pageSize) =>
        APage(1, pageSize, 100).TotalPages.ShouldBe(0);

    [Fact]
    public void HasNextPage_IsTrue_WhileFurtherPagesExist()
    {
        APage(1, 10, 25).HasNextPage.ShouldBeTrue();
        APage(2, 10, 25).HasNextPage.ShouldBeTrue();
    }

    [Fact]
    public void HasNextPage_IsFalse_OnTheLastPage() => APage(3, 10, 25).HasNextPage.ShouldBeFalse();

    [Fact]
    public void HasNextPage_IsFalse_BeyondTheLastPage() => APage(4, 10, 25).HasNextPage.ShouldBeFalse();

    [Fact]
    public void HasNextPage_IsFalse_WhenThereAreNoRowsAtAll() =>
        APage(1, 10, 0).HasNextPage.ShouldBeFalse();

    [Fact]
    public void HasNextPage_IsFalse_WhenThePageSizeIsNotPositive() =>
        APage(1, 0, 100).HasNextPage.ShouldBeFalse();
}

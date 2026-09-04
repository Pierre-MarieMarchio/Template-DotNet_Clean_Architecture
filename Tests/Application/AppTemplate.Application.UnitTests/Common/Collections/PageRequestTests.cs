using AppTemplate.Application.Common.Collections;
using Shouldly;
using Xunit;

namespace AppTemplate.Application.UnitTests.Common.Collections;

public sealed class PageRequestTests
{
    private static readonly FakeCollectionPolicy _policy = new();

    #region ParseMode

    [Fact]
    public void ParseMode_ABlankValue_IsOffset() =>
        PageRequest.ParseMode(null).Value.ShouldBe(PagingMode.Offset);

    [Theory]
    [InlineData("offset")]
    [InlineData("OFFSET")]
    public void ParseMode_Offset_IsCaseInsensitive(string raw) =>
        PageRequest.ParseMode(raw).Value.ShouldBe(PagingMode.Offset);

    [Theory]
    [InlineData("cursor")]
    [InlineData("CURSOR")]
    public void ParseMode_Cursor_IsCaseInsensitive(string raw) =>
        PageRequest.ParseMode(raw).Value.ShouldBe(PagingMode.Cursor);

    [Fact]
    public void ParseMode_AnUnrecognisedValue_IsRejected()
    {
        var result = PageRequest.ParseMode("sideways");

        result.IsFailure.ShouldBeTrue();
        result.Error!.Code.ShouldBe("paging.invalid");
        result.Error.Message.ShouldContain("offset");
        result.Error.Message.ShouldContain("cursor");
    }

    #endregion

    #region Page size

    [Fact]
    public void Create_ANullPageSize_UsesThePolicysDefault() =>
        PageRequest.Create(PagingMode.Offset, null, null, null, _policy).Value.PageSize.ShouldBe(_policy.DefaultPageSize);

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Create_APageSizeBelowOne_IsRejected(int pageSize)
    {
        var result = PageRequest.Create(PagingMode.Offset, 1, pageSize, null, _policy);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Code.ShouldBe("paging.invalid");
    }

    [Fact]
    public void Create_APageSizeAtTheCeiling_IsAccepted() =>
        PageRequest.Create(PagingMode.Offset, 1, _policy.MaxPageSize, null, _policy).IsSuccess.ShouldBeTrue();

    [Fact]
    public void Create_APageSizeOneOverTheCeiling_IsRejected()
    {
        var result = PageRequest.Create(PagingMode.Offset, 1, _policy.MaxPageSize + 1, null, _policy);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Code.ShouldBe("paging.invalid");
        result.Error.Message.ShouldContain(_policy.MaxPageSize.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    #endregion

    #region Offset mode

    [Fact]
    public void Create_Offset_ANullPage_DefaultsToOne() =>
        PageRequest.Create(PagingMode.Offset, null, 10, null, _policy).Value.Page.ShouldBe(1);

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Create_Offset_APageBelowOne_IsRejected(int page)
    {
        var result = PageRequest.Create(PagingMode.Offset, page, 10, null, _policy);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Code.ShouldBe("paging.invalid");
    }

    [Fact]
    public void Create_Offset_ANonNullCursor_IsRejected()
    {
        var cursor = Cursor.After(SortOrder.Parse("name", _policy).Value.Terms[0], "key", Guid.CreateVersion7());

        var result = PageRequest.Create(PagingMode.Offset, 1, 10, cursor, _policy);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Code.ShouldBe("paging.invalid");
        result.Error.Message.ShouldContain("cursor");
    }

    #endregion

    #region Cursor mode

    [Fact]
    public void Create_Cursor_ANullCursor_IsTheFirstPage()
    {
        var result = PageRequest.Create(PagingMode.Cursor, null, 10, null, _policy);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Cursor.ShouldBeNull();
        result.Value.Page.ShouldBeNull();
    }

    [Fact]
    public void Create_Cursor_ANonNullCursor_IsCarriedThrough()
    {
        var cursor = Cursor.After(SortOrder.Parse("name", _policy).Value.Terms[0], "key", Guid.CreateVersion7());

        var result = PageRequest.Create(PagingMode.Cursor, null, 10, cursor, _policy);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Cursor.ShouldBeSameAs(cursor);
    }

    [Fact]
    public void Create_Cursor_ANonNullPage_IsRejected()
    {
        var result = PageRequest.Create(PagingMode.Cursor, 1, 10, null, _policy);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Code.ShouldBe("paging.invalid");
        result.Error.Message.ShouldContain("alternatives");
    }

    #endregion

    [Fact]
    public void Create_Rejects_ANullPolicy() =>
        Should.Throw<ArgumentNullException>(() => PageRequest.Create(PagingMode.Offset, 1, 10, null, null!));
}

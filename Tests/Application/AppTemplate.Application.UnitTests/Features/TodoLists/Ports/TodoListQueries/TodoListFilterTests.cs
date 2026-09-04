using System.Globalization;
using AppTemplate.Application.Common.Collections;
using AppTemplate.Application.Features.TodoLists.Ports.TodoListQueries;
using Shouldly;
using Xunit;

namespace AppTemplate.Application.UnitTests.Features.TodoLists.Ports.TodoListQueries;

public sealed class TodoListFilterTests
{
    [Fact]
    public void Create_WithNothing_HasNoSearchOrDateBounds()
    {
        var result = TodoListFilter.Create(null, null, null);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Search.ShouldBeNull();
        result.Value.CreatedAfter.ShouldBeNull();
        result.Value.CreatedBefore.ShouldBeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_ABlankSearch_IsNotAFilter(string? search) =>
        TodoListFilter.Create(search, null, null).Value.Search.ShouldBeNull();

    [Fact]
    public void Create_ASearchTerm_IsCarriedThrough() =>
        TodoListFilter.Create("groceries", null, null).Value.Search!.Value.ShouldBe("groceries");

    [Fact]
    public void Create_ASearchTermOverMaxLength_IsRejected()
    {
        string tooLong = new string('a', SearchTerm.MaxLength + 1);

        var result = TodoListFilter.Create(tooLong, null, null);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Code.ShouldBe("filter.invalid");
    }

    [Fact]
    public void Create_ValidDates_AreParsed()
    {
        var after = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var before = new DateTimeOffset(2024, 12, 31, 0, 0, 0, TimeSpan.Zero);

        var result = TodoListFilter.Create(
            null,
            after.ToString("O", CultureInfo.InvariantCulture),
            before.ToString("O", CultureInfo.InvariantCulture));

        result.IsSuccess.ShouldBeTrue();
        result.Value.CreatedAfter.ShouldBe(after);
        result.Value.CreatedBefore.ShouldBe(before);
    }

    [Fact]
    public void Create_AnUnparseableCreatedAfter_IsRejected()
    {
        var result = TodoListFilter.Create(null, "not-a-date", null);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Code.ShouldBe("filter.invalid");
        result.Error.Message.ShouldContain("createdAfter");
    }

    [Fact]
    public void Create_AnUnparseableCreatedBefore_IsRejected()
    {
        var result = TodoListFilter.Create(null, null, "not-a-date");

        result.IsFailure.ShouldBeTrue();
        result.Error!.Code.ShouldBe("filter.invalid");
        result.Error.Message.ShouldContain("createdBefore");
    }

    /// <summary>An empty window is a caller mistake, not an empty page.</summary>
    [Fact]
    public void Create_CreatedAfterLaterThanCreatedBefore_IsRejected()
    {
        var after = new DateTimeOffset(2024, 6, 1, 0, 0, 0, TimeSpan.Zero);
        var before = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);

        var result = TodoListFilter.Create(
            null,
            after.ToString("O", CultureInfo.InvariantCulture),
            before.ToString("O", CultureInfo.InvariantCulture));

        result.IsFailure.ShouldBeTrue();
        result.Error!.Code.ShouldBe("filter.invalid");
    }

    [Fact]
    public void Create_CreatedAfterEqualToCreatedBefore_IsAccepted()
    {
        string iso = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero).ToString("O", CultureInfo.InvariantCulture);

        TodoListFilter.Create(null, iso, iso).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void None_HasNoFilterAtAll()
    {
        TodoListFilter.None.Search.ShouldBeNull();
        TodoListFilter.None.CreatedAfter.ShouldBeNull();
        TodoListFilter.None.CreatedBefore.ShouldBeNull();
    }
}

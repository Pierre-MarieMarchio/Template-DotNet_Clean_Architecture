using AppTemplate.Api.Features.TodoLists.Mapping;
using AppTemplate.Application.Common;
using AppTemplate.Application.Features.TodoLists.Dtos;
using Shouldly;
using Xunit;

namespace AppTemplate.Api.UnitTests.Features.TodoLists.Mapping;

/// <summary>
/// A hand-written mapper's only failure mode is a field nobody copied, so every test here asserts on
/// the whole shape rather than on the members that happen to be interesting.
/// </summary>
public sealed class TodoListMappingTests
{
    [Fact]
    public void ToResponse_Item_CopiesEveryField()
    {
        var item = new TodoItemDto(
            Guid.CreateVersion7(),
            "Buy milk",
            "semi-skimmed",
            IsCompleted: true,
            CompletedAt: new DateTimeOffset(2026, 3, 4, 5, 6, 7, TimeSpan.Zero),
            Tags: ["errand", "food"]);

        var response = TodoListMapping.ToResponse(item);

        response.Id.ShouldBe(item.Id);
        response.Title.ShouldBe(item.Title);
        response.Description.ShouldBe(item.Description);
        response.IsCompleted.ShouldBe(item.IsCompleted);
        response.CompletedAt.ShouldBe(item.CompletedAt);
        response.Tags.ShouldBe(item.Tags);
    }

    [Fact]
    public void ToResponse_Item_CarriesTheAbsentFields_AsAbsent()
    {
        var item = new TodoItemDto(
            Guid.CreateVersion7(),
            "Buy milk",
            Description: null,
            IsCompleted: false,
            CompletedAt: null,
            Tags: []);

        var response = TodoListMapping.ToResponse(item);

        response.Description.ShouldBeNull();
        response.IsCompleted.ShouldBeFalse();
        response.CompletedAt.ShouldBeNull();
        response.Tags.ShouldBeEmpty();
    }

    [Fact]
    public void ToResponse_List_CopiesEveryField()
    {
        var list = new TodoListDetailDto(
            Guid.CreateVersion7(),
            "Groceries",
            CreatedAt: new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero),
            LastModifiedAt: new DateTimeOffset(2026, 1, 3, 3, 4, 5, TimeSpan.Zero),
            Items: [AnItem("Buy milk")]);

        var response = TodoListMapping.ToResponse(list);

        response.Id.ShouldBe(list.Id);
        response.Name.ShouldBe(list.Name);
        response.CreatedAt.ShouldBe(list.CreatedAt);
        response.LastModifiedAt.ShouldBe(list.LastModifiedAt);
        response.Items.Single().Title.ShouldBe("Buy milk");
    }

    [Fact]
    public void ToResponse_List_CarriesANeverModifiedListAsNeverModified()
    {
        var list = new TodoListDetailDto(Guid.CreateVersion7(), "Groceries", DateTimeOffset.UnixEpoch, null, []);

        var response = TodoListMapping.ToResponse(list);

        response.LastModifiedAt.ShouldBeNull();
        response.Items.ShouldBeEmpty();
    }

    /// <summary>
    /// The DTO's items are ordered by title upstream, so re-ordering them here would silently discard
    /// a decision the query made.
    /// </summary>
    [Fact]
    public void ToResponse_List_KeepsTheItemsInTheOrderReceived()
    {
        var list = new TodoListDetailDto(
            Guid.CreateVersion7(),
            "Groceries",
            DateTimeOffset.UnixEpoch,
            null,
            Items: [AnItem("Apples"), AnItem("Bread"), AnItem("Cheese")]);

        var response = TodoListMapping.ToResponse(list);

        response.Items.Select(item => item.Title).ShouldBe(["Apples", "Bread", "Cheese"]);
    }

    [Fact]
    public void ToResponse_Items_CopiesEveryFieldOfEveryItem()
    {
        var item = new TodoItemDto(
            Guid.CreateVersion7(),
            "Buy milk",
            "semi-skimmed",
            IsCompleted: true,
            CompletedAt: new DateTimeOffset(2026, 3, 4, 5, 6, 7, TimeSpan.Zero),
            Tags: ["errand", "food"]);

        var response = TodoListMapping.ToResponse((IReadOnlyList<TodoItemDto>)[item]);

        var mapped = response.Items.Single();
        mapped.Id.ShouldBe(item.Id);
        mapped.Title.ShouldBe(item.Title);
        mapped.Description.ShouldBe(item.Description);
        mapped.IsCompleted.ShouldBe(item.IsCompleted);
        mapped.CompletedAt.ShouldBe(item.CompletedAt);
        mapped.Tags.ShouldBe(item.Tags);
    }

    [Fact]
    public void ToResponse_Items_KeepsTheItemsInTheOrderReceived()
    {
        IReadOnlyList<TodoItemDto> items = [AnItem("Apples"), AnItem("Bread"), AnItem("Cheese")];

        var response = TodoListMapping.ToResponse(items);

        response.Items.Select(item => item.Title).ShouldBe(["Apples", "Bread", "Cheese"]);
    }

    /// <summary>
    /// An item-less list is still an envelope carrying an empty array, not an absent one: a client
    /// reading <c>items</c> must never have to distinguish null from empty.
    /// </summary>
    [Fact]
    public void ToResponse_Items_WrapsAnEmptyCollection_InAnEmptyArray()
    {
        var response = TodoListMapping.ToResponse((IReadOnlyList<TodoItemDto>)[]);

        response.Items.ShouldNotBeNull();
        response.Items.ShouldBeEmpty();
    }

    [Fact]
    public void ToResponse_Summary_CopiesEveryField()
    {
        var summary = new TodoListSummaryDto(
            Guid.CreateVersion7(),
            "Groceries",
            ItemCount: 7,
            CompletedItemCount: 3,
            CreatedAt: new DateTimeOffset(2026, 5, 6, 7, 8, 9, TimeSpan.Zero));

        var response = TodoListMapping.ToResponse(summary);

        response.Id.ShouldBe(summary.Id);
        response.Name.ShouldBe(summary.Name);
        response.ItemCount.ShouldBe(summary.ItemCount);
        response.CompletedItemCount.ShouldBe(summary.CompletedItemCount);
        response.CreatedAt.ShouldBe(summary.CreatedAt);
    }

    [Fact]
    public void ToPageResponse_CarriesEveryMetadataMember_OfAnOffsetPage()
    {
        var page = PagedResult.Offset<TodoListSummaryDto>(
            [ASummary("Groceries")],
            page: 2,
            pageSize: 10,
            totalCount: 25);

        var response = TodoListMapping.ToPageResponse(page).Value;

        response.Items.Single().Name.ShouldBe("Groceries");
        response.PageSize.ShouldBe(10);
        response.Page.ShouldBe(2);
        response.TotalCount.ShouldBe(25);
        response.TotalPages.ShouldBe(3);
        response.HasNextPage.ShouldBeTrue();
        response.NextCursor.ShouldBeNull();
    }

    [Fact]
    public void ToPageResponse_CarriesEveryMetadataMember_OfACursorPage()
    {
        var page = PagedResult.Keyset<TodoListSummaryDto>([ASummary("Groceries")], pageSize: 10, nextCursor: "opaque");

        var response = TodoListMapping.ToPageResponse(page).Value;

        response.Items.Count.ShouldBe(1);
        response.PageSize.ShouldBe(10);
        response.Page.ShouldBeNull();
        response.TotalCount.ShouldBeNull();
        response.TotalPages.ShouldBeNull();
        response.HasNextPage.ShouldBeTrue();
        response.NextCursor.ShouldBe("opaque");
    }

    [Fact]
    public void ToPageResponse_KeepsThePageInTheOrderReceived()
    {
        var page = PagedResult.Keyset<TodoListSummaryDto>(
            [ASummary("Groceries"), ASummary("Hardware"), ASummary("Reading")],
            pageSize: 3,
            nextCursor: null);

        var response = TodoListMapping.ToPageResponse(page).Value;

        response.Items.Select(summary => summary.Name).ShouldBe(["Groceries", "Hardware", "Reading"]);
    }

    [Fact]
    public void ToListResponse_KeepsTheVersion()
    {
        var list = new TodoListDetailDto(Guid.CreateVersion7(), "Groceries", DateTimeOffset.UnixEpoch, null, []);

        var result = TodoListMapping.ToListResponse(Result.Success(new Versioned<TodoListDetailDto>(list, 12)));

        result.Value.Version.ShouldBe(12u);
        result.Value.Value.Id.ShouldBe(list.Id);
    }

    [Fact]
    public void ToItemResponse_KeepsTheVersion()
    {
        var item = AnItem("Buy milk");

        var result = TodoListMapping.ToItemResponse(Result.Success(new Versioned<TodoItemDto>(item, 4)));

        result.Value.Version.ShouldBe(4u);
        result.Value.Value.Id.ShouldBe(item.Id);
    }

    [Fact]
    public void ToItemsResponse_KeepsTheVersion()
    {
        IReadOnlyList<TodoItemDto> items = [AnItem("Buy milk")];

        var result = TodoListMapping.ToItemsResponse(Result.Success(new Versioned<IReadOnlyList<TodoItemDto>>(items, 9)));

        result.Value.Version.ShouldBe(9u);
        result.Value.Value.Items.Single().Title.ShouldBe("Buy milk");
    }

    /// <summary>
    /// <c>Result{T}.Value</c> throws on a failure, so a lift that reached for it before testing
    /// <c>IsFailure</c> would turn every mapped error into a 500.
    /// </summary>
    [Fact]
    public void ToPageResponse_PropagatesAFailure_WithoutThrowing()
    {
        var result = TodoListMapping.ToPageResponse(Result.Failure<PagedResult<TodoListSummaryDto>>(_someError));

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(_someError);
    }

    [Fact]
    public void ToListResponse_PropagatesAFailure_WithoutThrowing()
    {
        var result = TodoListMapping.ToListResponse(Result.Failure<Versioned<TodoListDetailDto>>(_someError));

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(_someError);
    }

    [Fact]
    public void ToItemResponse_PropagatesAFailure_WithoutThrowing()
    {
        var result = TodoListMapping.ToItemResponse(Result.Failure<Versioned<TodoItemDto>>(_someError));

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(_someError);
    }

    [Fact]
    public void ToItemsResponse_PropagatesAFailure_WithoutThrowing()
    {
        var result = TodoListMapping.ToItemsResponse(
            Result.Failure<Versioned<IReadOnlyList<TodoItemDto>>>(_someError));

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(_someError);
    }

    private static readonly Error _someError = Error.NotFound("todoList.notFound", "gone");

    private static TodoItemDto AnItem(string title) =>
        new(Guid.CreateVersion7(), title, null, false, null, []);

    private static TodoListSummaryDto ASummary(string name) =>
        new(Guid.CreateVersion7(), name, 0, 0, DateTimeOffset.UnixEpoch);
}

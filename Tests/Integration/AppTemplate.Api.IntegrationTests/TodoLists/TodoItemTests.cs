using System.Net;
using System.Net.Http.Json;
using AppTemplate.Api.Features.TodoLists.Contracts;
using AppTemplate.Api.IntegrationTests.Infrastructure;
using AppTemplate.Application.Features.TodoLists.Dtos;
using Shouldly;
using Xunit;

namespace AppTemplate.Api.IntegrationTests.TodoLists;

/// <summary>
/// An item is reachable only through its list, and tags are normalised and de-duplicated on the way
/// in. Making either an aggregate root of its own would let an item change without its list's
/// invariants ever being loaded.
/// </summary>
public sealed class TodoItemTests(ApiFixture fixture) : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task AddingAnItem_Returns201AndTheItemAppearsOnItsList()
    {
        var (client, _, _) = await SignInAsync();
        var listId = await CreateTodoListAsync(client, "Chores");

        using var added = await client.PostAsJsonAsync(
            $"{TodoListsRoute}/{listId}/items",
            new AddTodoItemRequest("  Wash the car  ", "  Before Friday  ", null),
            TestToken);

        added.StatusCode.ShouldBe(HttpStatusCode.Created);
        var itemId = await ApiJson.ReadGuidAsync(added, TestToken);

        var detail = await ReadDetailAsync(client, listId);
        var item = detail.Items.Single();

        item.Id.ShouldBe(itemId);
        item.Title.ShouldBe("Wash the car");
        item.Description.ShouldBe("Before Friday");
        item.IsCompleted.ShouldBeFalse();
        item.CompletedAt.ShouldBeNull();
    }

    [Fact]
    public async Task Tags_AreLowerCasedTrimmedAndDeduplicated()
    {
        var (client, _, _) = await SignInAsync();
        var listId = await CreateTodoListAsync(client, "Tagged");

        IReadOnlyList<string> tags = ["Urgent", " urgent ", "URGENT", "Home"];
        var itemId = await AddTodoItemAsync(client, listId, "Tagged item", tags);

        var detail = await ReadDetailAsync(client, listId);
        var item = detail.Items.Single(candidate => candidate.Id == itemId);

        item.Tags.ShouldBe(["urgent", "home"], ignoreOrder: true);

        // Asserted at the database too: the owned collection's key is (owner, value), so a duplicate
        // tag is unrepresentable there as well as in the domain.
        var stored = await Database.QueryAsync(
            """SELECT "Value" FROM todo."TodoItemTags" ORDER BY "Value" """,
            TestToken);

        stored.ShouldBe(["home", "urgent"]);
    }

    [Fact]
    public async Task ABlankTag_IsRejectedAsAValidationFailure()
    {
        var (client, _, _) = await SignInAsync();
        var listId = await CreateTodoListAsync(client, "Tagged");

        IReadOnlyList<string> tags = ["fine", "   "];

        using var response = await client.PostAsJsonAsync(
            $"{TodoListsRoute}/{listId}/items",
            new AddTodoItemRequest("Item", null, tags),
            TestToken);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ApiJson.ReadProblemAsync(response, TestToken)).Code.ShouldBe("todoList.validationFailed");

        // Nothing was written: the whole command is one transaction.
        (await Database.CountAsync("""SELECT count(*) FROM todo."TodoItems" """, TestToken)).ShouldBe(0);
        (await Database.CountAsync("""SELECT count(*) FROM todo."TodoItemTags" """, TestToken)).ShouldBe(0);
    }

    /// <summary>
    /// The aggregate boundary, over HTTP: the item exists, the caller owns both lists, and the item
    /// is still unreachable through the wrong one.
    /// </summary>
    [Fact]
    public async Task AnItem_IsNotReachableThroughADifferentListOfTheSameOwner()
    {
        var (client, _, _) = await SignInAsync();
        var listId = await CreateTodoListAsync(client, "Its list");
        var otherListId = await CreateTodoListAsync(client, "Another list");
        var itemId = await AddTodoItemAsync(client, listId, "Only here");

        using var completed = await client.PostAsync(
            new Uri($"{TodoListsRoute}/{otherListId}/items/{itemId}/complete", UriKind.Relative),
            content: null,
            TestToken);

        completed.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await ApiJson.ReadProblemAsync(completed, TestToken)).Code.ShouldBe("todoItem.notFound");

        using var removed = await client.DeleteAsync(
            new Uri($"{TodoListsRoute}/{otherListId}/items/{itemId}", UriKind.Relative),
            TestToken);

        removed.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await ApiJson.ReadProblemAsync(removed, TestToken)).Code.ShouldBe("todoItem.notFound");

        // Untouched.
        (await ReadDetailAsync(client, listId)).Items.Single().IsCompleted.ShouldBeFalse();
    }

    /// <summary>There is no route that names an item without naming its list.</summary>
    [Theory]
    [InlineData("/api/v1/todo-items")]
    [InlineData("/api/v1/todo-tags")]
    public async Task NoTopLevelRouteAddressesAnItemOrATag(string path)
    {
        var (client, _, _) = await SignInAsync();

        using var response = await client.GetAsync(new Uri(path, UriKind.Relative), TestToken);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task AnUnknownItemIdOnTheRightList_Is404()
    {
        var (client, _, _) = await SignInAsync();
        var listId = await CreateTodoListAsync(client, "Chores");

        using var response = await client.DeleteAsync(
            new Uri($"{TodoListsRoute}/{listId}/items/{Guid.CreateVersion7()}", UriKind.Relative),
            TestToken);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await ApiJson.ReadProblemAsync(response, TestToken)).Code.ShouldBe("todoItem.notFound");
    }

    [Fact]
    public async Task RemovingAnItem_Returns204AndTakesItsTagsWithIt()
    {
        var (client, _, _) = await SignInAsync();
        var listId = await CreateTodoListAsync(client, "Chores");
        IReadOnlyList<string> tags = ["urgent"];
        var itemId = await AddTodoItemAsync(client, listId, "Doomed", tags);

        using var response = await client.DeleteAsync(
            new Uri($"{TodoListsRoute}/{listId}/items/{itemId}", UriKind.Relative),
            TestToken);

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await ReadDetailAsync(client, listId)).Items.ShouldBeEmpty();
        (await Database.CountAsync("""SELECT count(*) FROM todo."TodoItemTags" """, TestToken)).ShouldBe(0);
    }

    [Fact]
    public async Task CompletingAnItem_Returns204AndStampsTheCompletionInstant()
    {
        var (client, _, _) = await SignInAsync();
        var listId = await CreateTodoListAsync(client, "Chores");
        var itemId = await AddTodoItemAsync(client, listId, "Do the thing");

        Clock.Advance(TimeSpan.FromMinutes(3));
        var completedAt = Clock.UtcNow;

        using var response = await client.PostAsync(
            new Uri($"{TodoListsRoute}/{listId}/items/{itemId}/complete", UriKind.Relative),
            content: null,
            TestToken);

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var item = (await ReadDetailAsync(client, listId)).Items.Single();
        item.IsCompleted.ShouldBeTrue();
        item.CompletedAt.ShouldBe(completedAt);
    }

    [Fact]
    public async Task TheItemsOfAList_AreOrderedByTitle()
    {
        var (client, _, _) = await SignInAsync();
        var listId = await CreateTodoListAsync(client, "Chores");

        await AddTodoItemAsync(client, listId, "Zebra");
        await AddTodoItemAsync(client, listId, "Apple");
        await AddTodoItemAsync(client, listId, "Mango");

        (await ReadDetailAsync(client, listId)).Items
            .Select(item => item.Title)
            .ShouldBe(["Apple", "Mango", "Zebra"]);
    }

    private static async Task<TodoListDetailDto> ReadDetailAsync(HttpClient client, Guid listId)
    {
        using var response = await client.GetAsync(
            new Uri($"{TodoListsRoute}/{listId}", UriKind.Relative),
            TestToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        return await ApiJson.ReadAsync<TodoListDetailDto>(response, TestToken);
    }
}

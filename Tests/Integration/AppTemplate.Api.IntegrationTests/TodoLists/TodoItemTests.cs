using System.Net;
using System.Net.Http.Json;
using AppTemplate.Api.Features.TodoLists.Contracts.Requests;
using AppTemplate.Api.Features.TodoLists.Contracts.Responses;
using AppTemplate.Api.IntegrationTests.Infrastructure;
using Shouldly;
using Xunit;

namespace AppTemplate.Api.IntegrationTests.TodoLists;

public sealed class TodoItemTests(ApiFixture fixture) : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task AddingAnItem_Returns201WithTheItemAndItAppearsOnItsList()
    {
        var (client, _, _) = await SignInAsync();
        var listId = await CreateTodoListAsync(client, "Chores");

        using var added = await client.PostAsJsonAsync(
            $"{TodoListsRoute}/{listId}/items",
            new AddTodoItemRequest("  Wash the car  ", "  Before Friday  ", null),
            TestToken);

        added.StatusCode.ShouldBe(HttpStatusCode.Created);

        var created = await ApiJson.ReadAsync<TodoItemResponse>(added, TestToken);
        created.Title.ShouldBe("Wash the car");
        created.Description.ShouldBe("Before Friday");
        added.Headers.ETag.ShouldNotBeNull();

        var detail = await ReadDetailAsync(client, listId);
        var item = detail.Items.Single();

        item.Id.ShouldBe(created.Id);
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
        (await ApiJson.ReadProblemAsync(response, TestToken)).Code.ShouldBe("request.validationFailed");

        (await Database.CountAsync("""SELECT count(*) FROM todo."TodoItems" """, TestToken)).ShouldBe(0);
        (await Database.CountAsync("""SELECT count(*) FROM todo."TodoItemTags" """, TestToken)).ShouldBe(0);
    }

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

        (await ReadDetailAsync(client, listId)).Items.Single().IsCompleted.ShouldBeFalse();
    }

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
    public async Task RemovingAnItem_AnswersWithTheListAndTakesItsTagsWithIt()
    {
        var (client, _, _) = await SignInAsync();
        var listId = await CreateTodoListAsync(client, "Chores");
        IReadOnlyList<string> tags = ["urgent"];
        var itemId = await AddTodoItemAsync(client, listId, "Doomed", tags);

        using var response = await client.DeleteAsync(
            new Uri($"{TodoListsRoute}/{listId}/items/{itemId}", UriKind.Relative),
            TestToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var list = await ApiJson.ReadAsync<TodoListResponse>(response, TestToken);
        list.Id.ShouldBe(listId);
        list.Items.ShouldBeEmpty();
        response.Headers.ETag.ShouldNotBeNull();

        (await ReadDetailAsync(client, listId)).Items.ShouldBeEmpty();
        (await Database.CountAsync("""SELECT count(*) FROM todo."TodoItemTags" """, TestToken)).ShouldBe(0);
    }

    [Fact]
    public async Task CompletingAnItem_AnswersWithTheItemAndStampsTheCompletionInstant()
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

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var completed = await ApiJson.ReadAsync<TodoItemResponse>(response, TestToken);
        completed.Id.ShouldBe(itemId);
        completed.IsCompleted.ShouldBeTrue();
        completed.CompletedAt.ShouldBe(completedAt);
        response.Headers.ETag.ShouldNotBeNull();

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

    private static async Task<TodoListResponse> ReadDetailAsync(HttpClient client, Guid listId)
    {
        using var response = await client.GetAsync(
            new Uri($"{TodoListsRoute}/{listId}", UriKind.Relative),
            TestToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        return await ApiJson.ReadAsync<TodoListResponse>(response, TestToken);
    }
}

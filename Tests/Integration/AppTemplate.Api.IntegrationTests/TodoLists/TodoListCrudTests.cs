using System.Net;
using System.Net.Http.Json;
using AppTemplate.Api.Common.Contracts;
using AppTemplate.Api.Features.TodoLists.Contracts.Requests;
using AppTemplate.Api.Features.TodoLists.Contracts.Responses;
using AppTemplate.Api.IntegrationTests.Infrastructure;
using Shouldly;
using Xunit;

namespace AppTemplate.Api.IntegrationTests.TodoLists;

public sealed class TodoListCrudTests(ApiFixture fixture) : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task Create_Returns201WithTheNewListItsETagAndAResolvableLocationHeader()
    {
        var (client, _, _) = await SignInAsync();

        using var created = await client.PostAsJsonAsync(
            TodoListsRoute,
            new CreateTodoListRequest("Groceries"),
            TestToken);

        created.StatusCode.ShouldBe(HttpStatusCode.Created);

        var list = await ApiJson.ReadAsync<TodoListResponse>(created, TestToken);
        list.Id.ShouldNotBe(Guid.Empty);
        list.Name.ShouldBe("Groceries");
        list.Items.ShouldBeEmpty();
        created.Headers.ETag.ShouldNotBeNull();

        created.Headers.Location.ShouldNotBeNull();
        created.Headers.Location!.ToString().ShouldEndWith(list.Id.ToString());

        using var followed = await client.GetAsync(created.Headers.Location, TestToken);

        followed.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await ApiJson.ReadAsync<TodoListResponse>(followed, TestToken)).Name.ShouldBe("Groceries");
    }

    [Fact]
    public async Task EveryResponse_CarriesEachMemberAsTheListHoldsIt()
    {
        var (client, _, _) = await SignInAsync();
        var createdAt = Clock.UtcNow;
        var listId = await CreateTodoListAsync(client, "Errands");

        using var added = await client.PostAsJsonAsync(
            $"{TodoListsRoute}/{listId}/items",
            new AddTodoItemRequest("Post a letter", "Before noon", ["urgent"]),
            TestToken);
        var itemId = (await ApiJson.ReadAsync<TodoItemResponse>(added, TestToken)).Id;
        await AddTodoItemAsync(client, listId, "Buy stamps");

        var completedAt = createdAt.AddMinutes(5);
        Clock.Set(completedAt);
        using var completed = await client.PostAsync(
            new Uri($"{TodoListsRoute}/{listId}/items/{itemId}/complete", UriKind.Relative),
            content: null,
            TestToken);
        completed.StatusCode.ShouldBe(HttpStatusCode.OK);

        using var read = await client.GetAsync(new Uri($"{TodoListsRoute}/{listId}", UriKind.Relative), TestToken);
        var list = await ApiJson.ReadAsync<TodoListResponse>(read, TestToken);
        list.Id.ShouldBe(listId);
        list.Name.ShouldBe("Errands");
        list.CreatedAt.ShouldBe(createdAt);
        list.LastModifiedAt.ShouldBe(completedAt);
        list.Items.Count.ShouldBe(2);

        using var single = await client.GetAsync(
            new Uri($"{TodoListsRoute}/{listId}/items/{itemId}", UriKind.Relative),
            TestToken);
        var item = await ApiJson.ReadAsync<TodoItemResponse>(single, TestToken);
        item.Id.ShouldBe(itemId);
        item.Title.ShouldBe("Post a letter");
        item.Description.ShouldBe("Before noon");
        item.IsCompleted.ShouldBeTrue();
        item.CompletedAt.ShouldBe(completedAt);
        item.Tags.ShouldBe(["urgent"]);

        using var items = await client.GetAsync(new Uri($"{TodoListsRoute}/{listId}/items", UriKind.Relative), TestToken);
        (await ApiJson.ReadAsync<TodoItemsResponse>(items, TestToken)).Items.Select(each => each.Title)
            .ShouldBe(["Post a letter", "Buy stamps"], ignoreOrder: true);

        using var listed = await client.GetAsync(new Uri(TodoListsRoute, UriKind.Relative), TestToken);
        listed.Headers.ETag.ShouldBeNull("a page has no version a write could name");

        var summary = (await ApiJson.ReadAsync<PagedResponse<TodoListSummaryResponse>>(listed, TestToken)).Items.ShouldHaveSingleItem();
        summary.Id.ShouldBe(listId);
        summary.Name.ShouldBe("Errands");
        summary.ItemCount.ShouldBe(2);
        summary.CompletedItemCount.ShouldBe(1);
        summary.CreatedAt.ShouldBe(createdAt);
    }

    [Fact]
    public async Task TheRoundTrip_CreateReadDeleteRead()
    {
        var (client, _, _) = await SignInAsync();

        var id = await CreateTodoListAsync(client, "Reading list");

        using var read = await client.GetAsync(new Uri($"{TodoListsRoute}/{id}", UriKind.Relative), TestToken);
        read.StatusCode.ShouldBe(HttpStatusCode.OK);

        var detail = await ApiJson.ReadAsync<TodoListResponse>(read, TestToken);
        detail.Id.ShouldBe(id);
        detail.Name.ShouldBe("Reading list");
        detail.Items.ShouldBeEmpty();
        detail.CreatedAt.ShouldBe(Clock.UtcNow);
        detail.LastModifiedAt.ShouldBeNull();

        using var deleted = await client.DeleteAsync(
            new Uri($"{TodoListsRoute}/{id}", UriKind.Relative),
            TestToken);
        deleted.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        using var readAgain = await client.GetAsync(new Uri($"{TodoListsRoute}/{id}", UriKind.Relative), TestToken);
        readAgain.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await ApiJson.ReadProblemAsync(readAgain, TestToken)).Code.ShouldBe("todoList.notFound");
    }

    [Fact]
    public async Task Rename_TakesTheIdFromTheRouteAndAnswersWithTheRenamedList()
    {
        var (client, _, _) = await SignInAsync();
        var id = await CreateTodoListAsync(client, "Before");

        using var renamed = await client.PutAsJsonAsync(
            $"{TodoListsRoute}/{id}",
            new RenameTodoListRequest("After"),
            TestToken);

        renamed.StatusCode.ShouldBe(HttpStatusCode.OK);

        var updated = await ApiJson.ReadAsync<TodoListResponse>(renamed, TestToken);
        updated.Id.ShouldBe(id);
        updated.Name.ShouldBe("After");
        renamed.Headers.ETag.ShouldNotBeNull();

        using var read = await client.GetAsync(new Uri($"{TodoListsRoute}/{id}", UriKind.Relative), TestToken);
        (await ApiJson.ReadAsync<TodoListResponse>(read, TestToken)).Name.ShouldBe("After");
    }

    [Fact]
    public async Task Delete_TakesTheItemsWithIt()
    {
        var (client, _, _) = await SignInAsync();
        var id = await CreateTodoListAsync(client, "Doomed");
        await AddTodoItemAsync(client, id, "An item");

        using var deleted = await client.DeleteAsync(
            new Uri($"{TodoListsRoute}/{id}", UriKind.Relative),
            TestToken);
        deleted.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        int items = await Database.CountAsync("SELECT count(*) FROM todo.\"TodoItems\"", TestToken);
        items.ShouldBe(0);
    }

    [Fact]
    public async Task DeletingAnAbsentList_Returns404WithTheStableCode()
    {
        var (client, _, _) = await SignInAsync();

        using var response = await client.DeleteAsync(
            new Uri($"{TodoListsRoute}/{Guid.CreateVersion7()}", UriKind.Relative),
            TestToken);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await ApiJson.ReadProblemAsync(response, TestToken)).Code.ShouldBe("todoList.notFound");
    }

    [Fact]
    public async Task ABlankName_IsRejectedAsAValidationFailure()
    {
        var (client, _, _) = await SignInAsync();

        using var response = await client.PostAsJsonAsync(
            TodoListsRoute,
            new CreateTodoListRequest("   "),
            TestToken);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var problem = await ApiJson.ReadProblemAsync(response, TestToken);
        problem.Code.ShouldBe("request.validationFailed");
        problem.Status.ShouldBe(400);
    }
}

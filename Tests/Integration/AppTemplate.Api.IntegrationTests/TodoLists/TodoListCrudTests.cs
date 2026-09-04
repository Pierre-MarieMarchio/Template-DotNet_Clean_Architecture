using System.Net;
using System.Net.Http.Json;
using AppTemplate.Api.Features.TodoLists.Contracts.Requests;
using AppTemplate.Api.Features.TodoLists.Contracts.Responses;
using AppTemplate.Api.IntegrationTests.Infrastructure;
using Shouldly;
using Xunit;

namespace AppTemplate.Api.IntegrationTests.TodoLists;

/// <summary>
/// The create / read / delete round trip over HTTP, with its status codes and <c>Location</c> header.
/// </summary>
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

        // The whole representation, so a caller that has just created a list can go on writing to it
        // without reading it back.
        var list = await ApiJson.ReadAsync<TodoListResponse>(created, TestToken);
        list.Id.ShouldNotBe(Guid.Empty);
        list.Name.ShouldBe("Groceries");
        list.Items.ShouldBeEmpty();
        created.Headers.ETag.ShouldNotBeNull();

        created.Headers.Location.ShouldNotBeNull();
        created.Headers.Location!.ToString().ShouldEndWith(list.Id.ToString());

        // The header is not decoration: following it has to reach the resource.
        using var followed = await client.GetAsync(created.Headers.Location, TestToken);

        followed.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await ApiJson.ReadAsync<TodoListResponse>(followed, TestToken)).Name.ShouldBe("Groceries");
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

        // Cascade, asserted at the database rather than inferred from the list being gone.
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

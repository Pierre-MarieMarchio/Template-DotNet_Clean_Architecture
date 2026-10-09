using System.Net;
using System.Net.Http.Json;
using AppTemplate.Api.Common.Contracts;
using AppTemplate.Api.Features.TodoLists.Contracts.Requests;
using AppTemplate.Api.Features.TodoLists.Contracts.Responses;
using AppTemplate.Api.IntegrationTests.Infrastructure;
using Shouldly;
using Xunit;

namespace AppTemplate.Api.IntegrationTests.Idempotency;

/// <summary>
/// The <c>Idempotency-Key</c> header over real HTTP against the real PostgreSQL fixture, on the two
/// actions that opt in: <c>TodoListsController.Create</c> and <c>AddItem</c>.
/// </summary>
public sealed class IdempotencyTests(ApiFixture fixture) : IntegrationTestBase(fixture)
{
    private const string _headerName = "Idempotency-Key";
    private const string _replayedHeaderName = "Idempotency-Replayed";

    [Fact]
    public async Task TheSameKeyTwice_CreatesOneList_AndReplaysTheFirstResponse()
    {
        var (client, _, _) = await SignInAsync();

        using var first = await PostAsync(client, "same-key", new CreateTodoListRequest("Groceries"));
        first.StatusCode.ShouldBe(HttpStatusCode.Created);
        first.Headers.Contains(_replayedHeaderName).ShouldBeFalse("the first request is not a replay.");

        var firstId = await ReadListIdAsync(first);
        var firstLocation = first.Headers.Location;

        using var second = await PostAsync(client, "same-key", new CreateTodoListRequest("Groceries"));

        second.StatusCode.ShouldBe(first.StatusCode);
        (await ReadListIdAsync(second)).ShouldBe(firstId);
        second.Headers.Location.ShouldBe(firstLocation);
        second.Headers.GetValues(_replayedHeaderName).ShouldContain("true");

        // The replay must carry a validator, and the same one the original response did — otherwise a
        // client that replays through a different instance than the one that served the first response
        // gets a body it cannot make a conditional request against.
        first.Headers.ETag.ShouldNotBeNull();
        second.Headers.ETag.ShouldBe(first.Headers.ETag);

        var lists = await GetListsAsync(client);
        lists.Items.Count.ShouldBe(1, "a replay must not create a second list.");
    }

    [Fact]
    public async Task TheSameKeyWithADifferentBody_IsRejectedAsKeyReused()
    {
        var (client, _, _) = await SignInAsync();

        using var first = await PostAsync(client, "reused-key", new CreateTodoListRequest("Groceries"));
        first.StatusCode.ShouldBe(HttpStatusCode.Created);

        using var second = await PostAsync(client, "reused-key", new CreateTodoListRequest("Something else"));

        second.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await ApiJson.ReadProblemAsync(second, TestToken)).Code.ShouldBe("idempotency.keyReused");

        var lists = await GetListsAsync(client);
        lists.Items.Count.ShouldBe(1, "the rejected retry must not have created anything.");
    }

    [Fact]
    public async Task TwoDifferentKeys_CreateTwoLists()
    {
        var (client, _, _) = await SignInAsync();

        using var first = await PostAsync(client, "key-one", new CreateTodoListRequest("First"));
        first.StatusCode.ShouldBe(HttpStatusCode.Created);

        using var second = await PostAsync(client, "key-two", new CreateTodoListRequest("Second"));
        second.StatusCode.ShouldBe(HttpStatusCode.Created);

        (await ReadListIdAsync(first)).ShouldNotBe(await ReadListIdAsync(second));

        var lists = await GetListsAsync(client);
        lists.Items.Count.ShouldBe(2);
    }

    [Fact]
    public async Task NoKeyAtAll_BehavesNormally_AndCreatesTwoLists()
    {
        var (client, _, _) = await SignInAsync();

        using var first = await client.PostAsJsonAsync(TodoListsRoute, new CreateTodoListRequest("First"), TestToken);
        first.StatusCode.ShouldBe(HttpStatusCode.Created);

        using var second = await client.PostAsJsonAsync(TodoListsRoute, new CreateTodoListRequest("Second"), TestToken);
        second.StatusCode.ShouldBe(HttpStatusCode.Created);

        (await ReadListIdAsync(first)).ShouldNotBe(await ReadListIdAsync(second));

        var lists = await GetListsAsync(client);
        lists.Items.Count.ShouldBe(2);
    }

    /// <summary>
    /// The important isolation test: the claim is scoped per user, so two different callers reusing
    /// the same key string as coincidence do not collide.
    /// </summary>
    [Fact]
    public async Task AKeyBelongingToADifferentUser_DoesNotCollide()
    {
        var (owner, _, _) = await SignInAsync("owner");
        var (stranger, _, _) = await SignInAsync("stranger");

        using var ownersList = await PostAsync(owner, "shared-key-string", new CreateTodoListRequest("Owner's list"));
        ownersList.StatusCode.ShouldBe(HttpStatusCode.Created);

        using var strangersList = await PostAsync(stranger, "shared-key-string", new CreateTodoListRequest("Stranger's list"));
        strangersList.StatusCode.ShouldBe(HttpStatusCode.Created);

        (await ReadListIdAsync(ownersList)).ShouldNotBe(await ReadListIdAsync(strangersList));

        (await GetListsAsync(owner)).Items.Count.ShouldBe(1);
        (await GetListsAsync(stranger)).Items.Count.ShouldBe(1);
    }

    /// <summary>
    /// A single space rather than an empty string: an empty header value is dropped before it ever
    /// reaches the wire, which would make this indistinguishable from sending no header at all.
    /// </summary>
    [Fact]
    public async Task ABlankKey_Returns400KeyInvalid()
    {
        var (client, _, _) = await SignInAsync();

        using var response = await PostAsync(client, " ", new CreateTodoListRequest("Groceries"));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ApiJson.ReadProblemAsync(response, TestToken)).Code.ShouldBe("idempotency.keyInvalid");
    }

    [Fact]
    public async Task AnOversizedKey_Returns400KeyInvalid()
    {
        var (client, _, _) = await SignInAsync();

        string oversized = new('a', 129); // the shipped default MaxKeyLength is 128.

        using var response = await PostAsync(client, oversized, new CreateTodoListRequest("Groceries"));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ApiJson.ReadProblemAsync(response, TestToken)).Code.ShouldBe("idempotency.keyInvalid");
    }

    /// <summary>Proves the claim is released on failure: a corrected retry under the same key succeeds.</summary>
    [Fact]
    public async Task AFailedRequest_ReleasesTheClaim_SoACorrectedRetryUnderTheSameKeySucceeds()
    {
        var (client, _, _) = await SignInAsync();

        using var failed = await PostAsync(client, "retry-after-failure", new CreateTodoListRequest(""));
        failed.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        using var corrected = await PostAsync(client, "retry-after-failure", new CreateTodoListRequest("Groceries"));

        corrected.StatusCode.ShouldBe(HttpStatusCode.Created);

        var lists = await GetListsAsync(client);
        lists.Items.Count.ShouldBe(1);
    }

    [Fact]
    public async Task AddItem_IsIdempotentToo_TheSameKeyTwiceAddsOneItem()
    {
        var (client, _, _) = await SignInAsync();
        var listId = await CreateTodoListAsync(client, "Groceries");

        var request = new AddTodoItemRequest("Buy milk", null, null);

        using var first = await PostAsync(client, "add-item-key", request, $"{TodoListsRoute}/{listId}/items");
        first.StatusCode.ShouldBe(HttpStatusCode.Created);

        using var second = await PostAsync(client, "add-item-key", request, $"{TodoListsRoute}/{listId}/items");
        second.StatusCode.ShouldBe(HttpStatusCode.Created);
        second.Headers.GetValues(_replayedHeaderName).ShouldContain("true");

        (await ReadItemIdAsync(first)).ShouldBe(await ReadItemIdAsync(second));

        var detail = await ReadDetailAsync(client, listId);
        detail.Items.Count.ShouldBe(1, "a replayed AddItem must not add a second item.");
    }

    // ---- Helpers ---------------------------------------------------------------------------------

    private static async Task<Guid> ReadListIdAsync(HttpResponseMessage response) =>
        (await ApiJson.ReadAsync<TodoListResponse>(response, TestToken)).Id;

    private static async Task<Guid> ReadItemIdAsync(HttpResponseMessage response) =>
        (await ApiJson.ReadAsync<TodoItemResponse>(response, TestToken)).Id;

    private static Task<HttpResponseMessage> PostAsync(
        HttpClient client,
        string idempotencyKey,
        object body,
        string? path = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path ?? TodoListsRoute)
        {
            Content = JsonContent.Create(body, body.GetType()),
        };

        request.Headers.TryAddWithoutValidation(_headerName, idempotencyKey);

        return client.SendAsync(request, TestToken);
    }

    private static async Task<PagedResponse<TodoListSummaryResponse>> GetListsAsync(HttpClient client)
    {
        using var response = await client.GetAsync(new Uri(TodoListsRoute, UriKind.Relative), TestToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        return await ApiJson.ReadAsync<PagedResponse<TodoListSummaryResponse>>(response, TestToken);
    }

    private static async Task<TodoListResponse> ReadDetailAsync(HttpClient client, Guid listId)
    {
        using var response = await client.GetAsync(new Uri($"{TodoListsRoute}/{listId}", UriKind.Relative), TestToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        return await ApiJson.ReadAsync<TodoListResponse>(response, TestToken);
    }
}

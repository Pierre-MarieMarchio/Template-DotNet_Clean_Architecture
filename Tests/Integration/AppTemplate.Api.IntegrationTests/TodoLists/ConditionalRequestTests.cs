using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using AppTemplate.Api.Features.TodoLists.Contracts.Requests;
using AppTemplate.Api.Features.TodoLists.Contracts.Responses;
using AppTemplate.Api.IntegrationTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace AppTemplate.Api.IntegrationTests.TodoLists;

public sealed class ConditionalRequestTests(ApiFixture fixture) : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task AReadOfAList_PublishesAStrongQuotedETag()
    {
        var (client, _, _) = await SignInAsync();
        var listId = await CreateTodoListAsync(client, "Groceries");

        using var response = await client.GetAsync(
            new Uri($"{TodoListsRoute}/{listId}", UriKind.Relative),
            TestToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Headers.ETag.ShouldNotBeNull();
        response.Headers.ETag!.IsWeak.ShouldBeFalse("If-Match uses strong comparison; a weak tag can never match.");
        response.Headers.ETag.Tag.ShouldStartWith("\"");
        response.Headers.ETag.Tag.ShouldEndWith("\"");
        response.Headers.ETag.Tag.Length.ShouldBeGreaterThan(2, "an empty tag identifies nothing.");
    }

    [Fact]
    public async Task TheETag_DoesNotPublishTheStoredVersionInReadableForm()
    {
        var (client, _, _) = await SignInAsync();
        var listId = await CreateTodoListAsync(client, "Groceries");

        string tag = await ReadETagAsync(client, listId);

        await using var scope = Fixture.Factory.Services.CreateAsyncScope();
        uint version = (await LoadTodoListAsync(scope.ServiceProvider, listId)).Version;

        tag.Contains(version.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal).ShouldBeFalse(
            "the stored version must not be legible in the tag.");
    }

    [Fact]
    public async Task AReadOfAnItem_PublishesTheListsETag()
    {
        var (client, _, _) = await SignInAsync();
        var listId = await CreateTodoListAsync(client, "Groceries");
        var itemId = await AddTodoItemAsync(client, listId, "Buy milk");

        string listTag = await ReadETagAsync(client, listId);

        using var response = await client.GetAsync(
            new Uri($"{TodoListsRoute}/{listId}/items/{itemId}", UriKind.Relative),
            TestToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Headers.ETag?.ToString().ShouldBe(listTag);
    }

    [Fact]
    public async Task TheETag_ChangesAfterASuccessfulWrite()
    {
        var (client, _, _) = await SignInAsync();
        var listId = await CreateTodoListAsync(client, "Groceries");

        string before = await ReadETagAsync(client, listId);

        using var renamed = await RenameAsync(client, listId, "Groceries (this week)", before);
        renamed.StatusCode.ShouldBe(HttpStatusCode.OK);

        (await ReadETagAsync(client, listId)).ShouldNotBe(
            before,
            "a validator that survived a write would let the next stale request through.");
    }

    [Fact]
    public async Task TheETag_ChangesWhenAnItemIsAdded()
    {
        var (client, _, _) = await SignInAsync();
        var listId = await CreateTodoListAsync(client, "Groceries");

        string before = await ReadETagAsync(client, listId);
        await AddTodoItemAsync(client, listId, "Buy milk");

        (await ReadETagAsync(client, listId)).ShouldNotBe(before);
    }

    [Fact]
    public async Task TwoDifferentLists_DoNotShareOneETag()
    {
        var (client, _, _) = await SignInAsync();
        var first = await CreateTodoListAsync(client, "First");
        var second = await CreateTodoListAsync(client, "Second");

        (await ReadETagAsync(client, first)).ShouldNotBe(await ReadETagAsync(client, second));
    }

    [Fact]
    public async Task TheCurrentETag_IsAcceptedOnEveryMutatingEndpoint()
    {
        var (client, _, _) = await SignInAsync();
        var listId = await CreateTodoListAsync(client, "Groceries");

        using var renamed = await RenameAsync(client, listId, "Renamed", await ReadETagAsync(client, listId));
        renamed.StatusCode.ShouldBe(HttpStatusCode.OK);

        using var added = await SendAsync(
            client,
            HttpMethod.Post,
            $"{TodoListsRoute}/{listId}/items",
            await ReadETagAsync(client, listId),
            new AddTodoItemRequest("Buy milk", null, null));
        added.StatusCode.ShouldBe(HttpStatusCode.Created);

        var itemId = (await ApiJson.ReadAsync<TodoItemResponse>(added, TestToken)).Id;

        using var updated = await SendAsync(
            client,
            HttpMethod.Put,
            $"{TodoListsRoute}/{listId}/items/{itemId}",
            await ReadETagAsync(client, listId),
            new UpdateTodoItemRequest("Buy oat milk", "Two litres"));
        updated.StatusCode.ShouldBe(HttpStatusCode.OK);

        using var tagged = await SendAsync(
            client,
            HttpMethod.Post,
            $"{TodoListsRoute}/{listId}/items/{itemId}/tags",
            await ReadETagAsync(client, listId),
            new AddTodoItemTagRequest("urgent"));
        tagged.StatusCode.ShouldBe(HttpStatusCode.OK);

        IReadOnlyList<string> replacement = ["alpha", "beta"];

        using var retagged = await SendAsync(
            client,
            HttpMethod.Put,
            $"{TodoListsRoute}/{listId}/items/{itemId}/tags",
            await ReadETagAsync(client, listId),
            new ReplaceTodoItemTagsRequest(replacement));
        retagged.StatusCode.ShouldBe(HttpStatusCode.OK);

        using var untagged = await SendAsync(
            client,
            HttpMethod.Delete,
            $"{TodoListsRoute}/{listId}/items/{itemId}/tags/alpha",
            await ReadETagAsync(client, listId));
        untagged.StatusCode.ShouldBe(HttpStatusCode.OK);

        using var completed = await SendAsync(
            client,
            HttpMethod.Post,
            $"{TodoListsRoute}/{listId}/items/{itemId}/complete",
            await ReadETagAsync(client, listId));
        completed.StatusCode.ShouldBe(HttpStatusCode.OK);

        using var reopened = await SendAsync(
            client,
            HttpMethod.Post,
            $"{TodoListsRoute}/{listId}/items/{itemId}/reopen",
            await ReadETagAsync(client, listId));
        reopened.StatusCode.ShouldBe(HttpStatusCode.OK);

        using var removed = await SendAsync(
            client,
            HttpMethod.Delete,
            $"{TodoListsRoute}/{listId}/items/{itemId}",
            await ReadETagAsync(client, listId));
        removed.StatusCode.ShouldBe(HttpStatusCode.OK);

        using var deleted = await SendAsync(
            client,
            HttpMethod.Delete,
            $"{TodoListsRoute}/{listId}",
            await ReadETagAsync(client, listId));
        deleted.StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task AStaleETag_IsRefusedByEveryMutatingEndpoint()
    {
        var (client, _, _) = await SignInAsync();
        var listId = await CreateTodoListAsync(client, "Groceries");
        IReadOnlyList<string> tags = ["urgent"];
        var itemId = await AddTodoItemAsync(client, listId, "Buy milk", tags);

        string stale = await ReadETagAsync(client, listId);
        using var interfering = await RenameAsync(client, listId, "Renamed by somebody else");
        interfering.StatusCode.ShouldBe(HttpStatusCode.OK);

        using var renamed = await RenameAsync(client, listId, "Renamed by the stale caller", stale);
        await AssertPreconditionFailedAsync(renamed);

        using var added = await SendAsync(
            client,
            HttpMethod.Post,
            $"{TodoListsRoute}/{listId}/items",
            stale,
            new AddTodoItemRequest("Buy bread", null, null));
        await AssertPreconditionFailedAsync(added);

        using var updated = await SendAsync(
            client,
            HttpMethod.Put,
            $"{TodoListsRoute}/{listId}/items/{itemId}",
            stale,
            new UpdateTodoItemRequest("Buy oat milk", null));
        await AssertPreconditionFailedAsync(updated);

        using var tagged = await SendAsync(
            client,
            HttpMethod.Post,
            $"{TodoListsRoute}/{listId}/items/{itemId}/tags",
            stale,
            new AddTodoItemTagRequest("later"));
        await AssertPreconditionFailedAsync(tagged);

        IReadOnlyList<string> replacement = ["later"];

        using var retagged = await SendAsync(
            client,
            HttpMethod.Put,
            $"{TodoListsRoute}/{listId}/items/{itemId}/tags",
            stale,
            new ReplaceTodoItemTagsRequest(replacement));
        await AssertPreconditionFailedAsync(retagged);

        using var untagged = await SendAsync(
            client,
            HttpMethod.Delete,
            $"{TodoListsRoute}/{listId}/items/{itemId}/tags/urgent",
            stale);
        await AssertPreconditionFailedAsync(untagged);

        using var completed = await SendAsync(
            client,
            HttpMethod.Post,
            $"{TodoListsRoute}/{listId}/items/{itemId}/complete",
            stale);
        await AssertPreconditionFailedAsync(completed);

        using var reopened = await SendAsync(
            client,
            HttpMethod.Post,
            $"{TodoListsRoute}/{listId}/items/{itemId}/reopen",
            stale);
        await AssertPreconditionFailedAsync(reopened);

        using var removed = await SendAsync(
            client,
            HttpMethod.Delete,
            $"{TodoListsRoute}/{listId}/items/{itemId}",
            stale);
        await AssertPreconditionFailedAsync(removed);

        using var deleted = await SendAsync(client, HttpMethod.Delete, $"{TodoListsRoute}/{listId}", stale);
        await AssertPreconditionFailedAsync(deleted);

        var detail = await ReadDetailAsync(client, listId);
        detail.Name.ShouldBe("Renamed by somebody else");
        detail.Items.Select(item => item.Title).ShouldBe(["Buy milk"]);

        var item = detail.Items.Single();
        item.IsCompleted.ShouldBeFalse();
        item.Tags.ShouldBe(["urgent"]);
    }

    [Fact]
    public async Task A412_IsAProblemDocumentCarryingTheSecurityHeaders()
    {
        var (client, _, _) = await SignInAsync();
        var listId = await CreateTodoListAsync(client, "Groceries");

        string stale = await ReadETagAsync(client, listId);
        using var interfering = await RenameAsync(client, listId, "Renamed");
        interfering.StatusCode.ShouldBe(HttpStatusCode.OK);

        using var response = await RenameAsync(client, listId, "Too late", stale);

        var problem = await AssertPreconditionFailedAsync(response);
        problem.Title.ShouldBe("Precondition failed", problem.Body);
        problem.Detail.ShouldNotBeNullOrWhiteSpace(problem.Body);

        SecurityHeaderAssertions.AssertSecurityHeaders(response);
    }

    [Fact]
    public async Task AWeakValidator_NeverMatches()
    {
        var (client, _, _) = await SignInAsync();
        var listId = await CreateTodoListAsync(client, "Groceries");

        string current = await ReadETagAsync(client, listId);

        using var response = await RenameAsync(client, listId, "Renamed", $"W/{current}");

        await AssertPreconditionFailedAsync(response);
        (await ReadDetailAsync(client, listId)).Name.ShouldBe("Groceries");
    }

    [Fact]
    public async Task AQuotedTagThisApiNeverIssued_IsRefused()
    {
        var (client, _, _) = await SignInAsync();
        var listId = await CreateTodoListAsync(client, "Groceries");

        using var response = await RenameAsync(client, listId, "Renamed", "\"not-a-tag-from-here\"");

        await AssertPreconditionFailedAsync(response);
        (await ReadDetailAsync(client, listId)).Name.ShouldBe("Groceries");
    }

    [Theory]
    [InlineData("12345")]
    [InlineData("\"unterminated")]
    [InlineData("not-an-etag")]
    public async Task AMalformedIfMatch_IsRejectedAsABadRequest(string ifMatch)
    {
        var (client, _, _) = await SignInAsync();
        var listId = await CreateTodoListAsync(client, "Groceries");

        using var response = await RenameAsync(client, listId, "Renamed", ifMatch);

        response.StatusCode.ShouldBe(
            HttpStatusCode.BadRequest,
            "an If-Match this API cannot parse must not be ignored: " +
            await response.Content.ReadAsStringAsync(TestToken));

        var problem = await ApiJson.ReadProblemAsync(response, TestToken);
        problem.Code.ShouldBe("precondition.malformed", problem.Body);

        (await ReadDetailAsync(client, listId)).Name.ShouldBe("Groceries");
    }

    [Fact]
    public async Task AListOfTagsMatchesWhenOneOfThemIsCurrent()
    {
        var (client, _, _) = await SignInAsync();
        var listId = await CreateTodoListAsync(client, "Groceries");

        string current = await ReadETagAsync(client, listId);

        using var response = await RenameAsync(client, listId, "Renamed", $"\"nope\", {current}");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await ReadDetailAsync(client, listId)).Name.ShouldBe("Renamed");
    }

    [Fact]
    public async Task AWildcardIfMatch_SucceedsWhenTheResourceExists()
    {
        var (client, _, _) = await SignInAsync();
        var listId = await CreateTodoListAsync(client, "Groceries");

        using var response = await RenameAsync(client, listId, "Renamed", "*");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await ReadDetailAsync(client, listId)).Name.ShouldBe("Renamed");
    }

    [Fact]
    public async Task AWildcardIfMatch_FailsAgainstAResourceThatDoesNotExist()
    {
        var (client, _, _) = await SignInAsync();

        using var response = await RenameAsync(client, Guid.CreateVersion7(), "Renamed", "*");

        await AssertPreconditionFailedAsync(response);
    }

    [Fact]
    public async Task AWildcardIfMatch_FailsAgainstSomebodyElsesList()
    {
        var (owner, _, _) = await SignInAsync("owner");
        var listId = await CreateTodoListAsync(owner, "Groceries");

        var (stranger, _, _) = await SignInAsync("stranger");

        using var response = await RenameAsync(stranger, listId, "Renamed by a stranger", "*");

        await AssertPreconditionFailedAsync(response);
        (await ReadDetailAsync(owner, listId)).Name.ShouldBe("Groceries");
    }

    [Fact]
    public async Task WithoutAnIfMatch_AMissingListIsStillANotFound()
    {
        var (client, _, _) = await SignInAsync();

        using var response = await RenameAsync(client, Guid.CreateVersion7(), "Renamed");

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ARepeatedReadWithTheSameETag_IsA304WithNoBody()
    {
        var (client, _, _) = await SignInAsync();
        var listId = await CreateTodoListAsync(client, "Groceries");

        string tag = await ReadETagAsync(client, listId);

        using var response = await SendAsync(
            client,
            HttpMethod.Get,
            $"{TodoListsRoute}/{listId}",
            ifNoneMatch: tag);

        response.StatusCode.ShouldBe(HttpStatusCode.NotModified);
        response.Headers.ETag?.ToString().ShouldBe(tag, "a 304 must carry the validator it is answering with.");
        (await response.Content.ReadAsStringAsync(TestToken)).ShouldBeEmpty();
    }

    [Fact]
    public async Task AReadWithAStaleETag_ReturnsTheRepresentation()
    {
        var (client, _, _) = await SignInAsync();
        var listId = await CreateTodoListAsync(client, "Groceries");

        string stale = await ReadETagAsync(client, listId);
        using var renamed = await RenameAsync(client, listId, "Renamed");
        renamed.StatusCode.ShouldBe(HttpStatusCode.OK);

        using var response = await SendAsync(
            client,
            HttpMethod.Get,
            $"{TodoListsRoute}/{listId}",
            ifNoneMatch: stale);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await ApiJson.ReadAsync<TodoListResponse>(response, TestToken)).Name.ShouldBe("Renamed");
    }

    [Fact]
    public async Task ARepeatedReadOfAnItemWithTheSameETag_IsA304()
    {
        var (client, _, _) = await SignInAsync();
        var listId = await CreateTodoListAsync(client, "Groceries");
        var itemId = await AddTodoItemAsync(client, listId, "Buy milk");

        string tag = await ReadETagAsync(client, listId);

        using var response = await SendAsync(
            client,
            HttpMethod.Get,
            $"{TodoListsRoute}/{listId}/items/{itemId}",
            ifNoneMatch: tag);

        response.StatusCode.ShouldBe(HttpStatusCode.NotModified);
    }

    private static async Task<ProblemResponse> AssertPreconditionFailedAsync(HttpResponseMessage response)
    {
        response.StatusCode.ShouldBe(
            HttpStatusCode.PreconditionFailed,
            "a write whose condition does not hold must be refused: " +
            await response.Content.ReadAsStringAsync(TestToken));

        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");

        var problem = await ApiJson.ReadProblemAsync(response, TestToken);

        problem.Status.ShouldBe((int)HttpStatusCode.PreconditionFailed, problem.Body);
        problem.Code.ShouldBe(
            "precondition.failed",
            "clients branch on the code: a stale write has to be distinguishable from every other "
            + "refusal. " + problem.Body);

        return problem;
    }

    private static Task<HttpResponseMessage> SendAsync(
        HttpClient client,
        HttpMethod method,
        string path,
        string? ifMatch = null,
        object? body = null,
        string? ifNoneMatch = null)
    {
        var request = new HttpRequestMessage(method, path);

        if (body is not null)
        {
            request.Content = JsonContent.Create(body, body.GetType());
        }

        if (ifMatch is not null)
        {
            request.Headers.TryAddWithoutValidation("If-Match", ifMatch);
        }

        if (ifNoneMatch is not null)
        {
            request.Headers.TryAddWithoutValidation("If-None-Match", ifNoneMatch);
        }

        return client.SendAsync(request, TestToken);
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

using System.Net;
using System.Net.Http.Json;
using AppTemplate.Api.Features.TodoLists.Contracts.Requests;
using AppTemplate.Api.IntegrationTests.Infrastructure;
using Shouldly;
using Xunit;

namespace AppTemplate.Api.IntegrationTests.Caching;

/// <summary>
/// Every read states whether its response may be stored, and every write leaves the question alone.
/// </summary>
/// <remarks>
/// See <c>docs/adr/0019-caching-is-revalidation-not-storage.md</c>. <c>private</c> confines storage to
/// the end client; <c>no-cache</c> still lets that client store the response, but only reuse it after
/// revalidating with the origin — which is what makes the strong <c>ETag</c> this API already
/// publishes worth having.
/// </remarks>
public sealed class CacheHeaderTests(ApiFixture fixture) : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task AGetOfAList_CarriesTheCacheHeader()
    {
        var (client, _, _) = await SignInAsync();

        using var response = await client.GetAsync(new Uri(TodoListsRoute, UriKind.Relative), TestToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        AssertRevalidateOnly(response);
    }

    [Fact]
    public async Task AGetOfAnItem_CarriesTheCacheHeader()
    {
        var (client, _, _) = await SignInAsync();
        var listId = await CreateTodoListAsync(client, "Groceries");

        using var response = await client.GetAsync(
            new Uri($"{TodoListsRoute}/{listId}", UriKind.Relative),
            TestToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        AssertRevalidateOnly(response);
    }

    /// <summary>
    /// The 304 path in particular: a revalidation response is exactly where the header does its
    /// work, so it must not have been dropped on the short-circuit that produces it.
    /// </summary>
    [Fact]
    public async Task A304Revalidation_CarriesTheCacheHeaderToo()
    {
        var (client, _, _) = await SignInAsync();
        var listId = await CreateTodoListAsync(client, "Groceries");
        string tag = await ReadETagAsync(client, listId);

        var request = new HttpRequestMessage(HttpMethod.Get, $"{TodoListsRoute}/{listId}");
        request.Headers.TryAddWithoutValidation("If-None-Match", tag);

        using var response = await client.SendAsync(request, TestToken);

        response.StatusCode.ShouldBe(HttpStatusCode.NotModified);
        AssertRevalidateOnly(response);
    }

    [Fact]
    public async Task ACreate_DoesNotCarryTheCacheHeader()
    {
        var (client, _, _) = await SignInAsync();

        using var response = await client.PostAsJsonAsync(
            TodoListsRoute,
            new CreateTodoListRequest("Groceries"),
            TestToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        response.Headers.Contains("Cache-Control").ShouldBeFalse(
            "a write is not a cacheable representation; nothing here states otherwise for it.");
    }

    [Fact]
    public async Task ARename_DoesNotCarryTheCacheHeader()
    {
        var (client, _, _) = await SignInAsync();
        var listId = await CreateTodoListAsync(client, "Groceries");

        using var response = await RenameAsync(client, listId, "Renamed", await ReadETagAsync(client, listId));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Headers.Contains("Cache-Control").ShouldBeFalse();
    }

    [Fact]
    public async Task ADelete_DoesNotCarryTheCacheHeader()
    {
        var (client, _, _) = await SignInAsync();
        var listId = await CreateTodoListAsync(client, "Groceries");

        using var response = await client.DeleteAsync(
            new Uri($"{TodoListsRoute}/{listId}", UriKind.Relative),
            TestToken);

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        response.Headers.Contains("Cache-Control").ShouldBeFalse();
    }

    private static void AssertRevalidateOnly(HttpResponseMessage response)
    {
        var cacheControl = response.Headers.CacheControl;

        cacheControl.ShouldNotBeNull(
            "a read must state whether it may be stored: " +
            string.Join(", ", response.Headers.Select(header => header.Key)));
        cacheControl.Private.ShouldBeTrue("a shared cache must never be allowed to hold this response.");
        cacheControl.NoCache.ShouldBeTrue(
            "storage is allowed, but only reuse after revalidating — otherwise the ETag this API "
            + "publishes is a validator with nothing that ever calls it.");
        cacheControl.NoStore.ShouldBeFalse("no-store would forbid the very storage revalidation depends on.");
        cacheControl.Public.ShouldBeFalse();
    }
}

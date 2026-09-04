using System.Net;
using System.Net.Http.Json;
using AppTemplate.Api.Common.Contracts;
using AppTemplate.Api.Features.TodoLists.Contracts.Requests;
using AppTemplate.Api.Features.TodoLists.Contracts.Responses;
using AppTemplate.Api.IntegrationTests.Infrastructure;
using Shouldly;
using Xunit;

namespace AppTemplate.Api.IntegrationTests.Security;

/// <summary>
/// One user's aggregate is invisible to another, across the HTTP boundary.
/// </summary>
/// <remarks>
/// <para>
/// Every attempt answers <b>404, not 403</b>, and that is the right choice: 403 would confirm that
/// the id exists, which turns any endpoint taking a list id into an oracle for enumerating other
/// users' ids. The two cases — "no such list" and "not yours" — are deliberately indistinguishable,
/// and the tests below assert that indistinguishability rather than just the status code.
/// </para>
/// </remarks>
public sealed class OwnershipIsolationTests(ApiFixture fixture) : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task AnotherUsersList_IsNotReadable()
    {
        var (owner, intruder, listId, _) = await TwoUsersAndAListAsync();

        using var response = await intruder.GetAsync(
            new Uri($"{TodoListsRoute}/{listId}", UriKind.Relative),
            TestToken);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await ApiJson.ReadProblemAsync(response, TestToken)).Code.ShouldBe("todoList.notFound");

        // The owner still sees it, so the 404 is about the caller and not about the list.
        using var ownerResponse = await owner.GetAsync(
            new Uri($"{TodoListsRoute}/{listId}", UriKind.Relative),
            TestToken);
        ownerResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task AnotherUsersList_IsNotRenameable()
    {
        var (owner, intruder, listId, _) = await TwoUsersAndAListAsync();

        using var response = await intruder.PutAsJsonAsync(
            $"{TodoListsRoute}/{listId}",
            new RenameTodoListRequest("Hijacked"),
            TestToken);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await ApiJson.ReadProblemAsync(response, TestToken)).Code.ShouldBe("todoList.notFound");

        await TheListIsStillNamedAsync(owner, listId, "Owner's list");
    }

    [Fact]
    public async Task AnotherUsersList_IsNotDeletable()
    {
        var (owner, intruder, listId, _) = await TwoUsersAndAListAsync();

        using var response = await intruder.DeleteAsync(
            new Uri($"{TodoListsRoute}/{listId}", UriKind.Relative),
            TestToken);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await ApiJson.ReadProblemAsync(response, TestToken)).Code.ShouldBe("todoList.notFound");

        await TheListIsStillNamedAsync(owner, listId, "Owner's list");
    }

    [Fact]
    public async Task AnotherUsersList_CannotTakeNewItems()
    {
        var (owner, intruder, listId, _) = await TwoUsersAndAListAsync();

        using var response = await intruder.PostAsJsonAsync(
            $"{TodoListsRoute}/{listId}/items",
            new AddTodoItemRequest("Smuggled", null, null),
            TestToken);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await ApiJson.ReadProblemAsync(response, TestToken)).Code.ShouldBe("todoList.notFound");

        using var read = await owner.GetAsync(new Uri($"{TodoListsRoute}/{listId}", UriKind.Relative), TestToken);
        var detail = await ApiJson.ReadAsync<TodoListResponse>(read, TestToken);
        detail.Items.Select(item => item.Title).ShouldNotContain("Smuggled");
    }

    [Fact]
    public async Task AnItemOnAnotherUsersList_CannotBeCompletedOrRemoved()
    {
        var (owner, intruder, listId, itemId) = await TwoUsersAndAListAsync();

        using var completed = await intruder.PostAsync(
            new Uri($"{TodoListsRoute}/{listId}/items/{itemId}/complete", UriKind.Relative),
            content: null,
            TestToken);

        completed.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await ApiJson.ReadProblemAsync(completed, TestToken)).Code.ShouldBe("todoList.notFound");

        using var removed = await intruder.DeleteAsync(
            new Uri($"{TodoListsRoute}/{listId}/items/{itemId}", UriKind.Relative),
            TestToken);

        removed.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await ApiJson.ReadProblemAsync(removed, TestToken)).Code.ShouldBe("todoList.notFound");

        using var read = await owner.GetAsync(new Uri($"{TodoListsRoute}/{listId}", UriKind.Relative), TestToken);
        var detail = await ApiJson.ReadAsync<TodoListResponse>(read, TestToken);
        detail.Items.Single().IsCompleted.ShouldBeFalse();
    }

    /// <summary>
    /// The point of answering 404: an intruder cannot tell an id that exists from one that does not,
    /// so comparing the two responses tells them nothing.
    /// </summary>
    [Fact]
    public async Task AnExistingForeignList_AnswersExactlyAsAnAbsentOneDoes()
    {
        var (_, intruder, listId, _) = await TwoUsersAndAListAsync();

        using var foreignResponse = await intruder.GetAsync(
            new Uri($"{TodoListsRoute}/{listId}", UriKind.Relative),
            TestToken);
        using var absentResponse = await intruder.GetAsync(
            new Uri($"{TodoListsRoute}/{Guid.CreateVersion7()}", UriKind.Relative),
            TestToken);

        var foreign = await ApiJson.ReadProblemAsync(foreignResponse, TestToken);
        var absent = await ApiJson.ReadProblemAsync(absentResponse, TestToken);

        foreignResponse.StatusCode.ShouldBe(absentResponse.StatusCode);
        foreign.Code.ShouldBe(absent.Code);
        foreign.Title.ShouldBe(absent.Title);

        // Detail names the id that was asked for, which is the caller's own input, so the two differ
        // only in that echoed id and in nothing that could disclose existence.
        foreign.Detail.ShouldBe($"No to-do list with id '{listId}' was found.");
    }

    [Fact]
    public async Task TheIndex_ShowsOnlyTheCallersOwnLists()
    {
        var (owner, intruder, listId, _) = await TwoUsersAndAListAsync();

        await CreateTodoListAsync(intruder, "Intruder's list");

        using var intruderIndex = await intruder.GetAsync(new Uri(TodoListsRoute, UriKind.Relative), TestToken);
        var page = await ApiJson.ReadAsync<PagedResponse<TodoListSummaryResponse>>(intruderIndex, TestToken);

        page.TotalCount.ShouldBe(1);
        page.Items.Single().Name.ShouldBe("Intruder's list");
        page.Items.Select(item => item.Id).ShouldNotContain(listId);

        using var ownerIndex = await owner.GetAsync(new Uri(TodoListsRoute, UriKind.Relative), TestToken);
        var ownerPage = await ApiJson.ReadAsync<PagedResponse<TodoListSummaryResponse>>(ownerIndex, TestToken);

        ownerPage.TotalCount.ShouldBe(1);
        ownerPage.Items.Single().Id.ShouldBe(listId);
    }

    private async Task<(HttpClient Owner, HttpClient Intruder, Guid ListId, Guid ItemId)> TwoUsersAndAListAsync()
    {
        var (owner, _, _) = await SignInAsync("owner");
        var (intruder, _, _) = await SignInAsync("intruder");

        var listId = await CreateTodoListAsync(owner, "Owner's list");
        var itemId = await AddTodoItemAsync(owner, listId, "Owner's item");

        return (owner, intruder, listId, itemId);
    }

    private static async Task TheListIsStillNamedAsync(HttpClient owner, Guid listId, string expected)
    {
        using var response = await owner.GetAsync(
            new Uri($"{TodoListsRoute}/{listId}", UriKind.Relative),
            TestToken);

        (await ApiJson.ReadAsync<TodoListResponse>(response, TestToken)).Name.ShouldBe(expected);
    }
}

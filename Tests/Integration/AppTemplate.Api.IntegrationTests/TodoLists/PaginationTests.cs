using System.Globalization;
using System.Net;
using AppTemplate.Api.IntegrationTests.Infrastructure;
using AppTemplate.Application.Common;
using AppTemplate.Application.Features.TodoLists.Collections;
using AppTemplate.Application.Features.TodoLists.Dtos;
using Shouldly;
using Xunit;

namespace AppTemplate.Api.IntegrationTests.TodoLists;

/// <summary>
/// The page bounds are enforced and <c>totalCount</c> is the total, not the page's size — a count
/// taken from the materialised page would report the latter.
/// </summary>
public sealed class PaginationTests(ApiFixture fixture) : IntegrationTestBase(fixture)
{
    private const int _listCount = 5;

    [Fact]
    public async Task TotalCountIsTheTotal_NotThePageSize()
    {
        var (client, _, _) = await SignInAsync();
        await CreateListsAsync(client, _listCount);

        var page = await ReadPageAsync(client, page: 1, pageSize: 2);

        page.Items.Count.ShouldBe(2);
        page.TotalCount.ShouldBe(_listCount);
        page.Page.ShouldBe(1);
        page.PageSize.ShouldBe(2);
        page.TotalPages.ShouldBe(3);
        page.HasNextPage.ShouldBeTrue();
    }

    [Fact]
    public async Task ThePagesTogether_CoverEveryRowExactlyOnce()
    {
        var (client, _, _) = await SignInAsync();
        await CreateListsAsync(client, _listCount);

        var seen = new List<Guid>();

        for (int page = 1; page <= 3; page++)
        {
            var result = await ReadPageAsync(client, page, pageSize: 2);
            seen.AddRange(result.Items.Select(item => item.Id));
        }

        // No row shown twice and none missed, which is what the unique tiebreaker in the ordering is
        // there to guarantee — several lists here are created in the same clock instant.
        seen.Count.ShouldBe(_listCount);
        seen.Distinct().Count().ShouldBe(_listCount);
    }

    [Fact]
    public async Task TheLastPage_ReportsNoNextPage()
    {
        var (client, _, _) = await SignInAsync();
        await CreateListsAsync(client, _listCount);

        var page = await ReadPageAsync(client, page: 3, pageSize: 2);

        page.Items.Count.ShouldBe(1);
        page.HasNextPage.ShouldBeFalse();
    }

    [Fact]
    public async Task APageBeyondTheEnd_IsEmptyButStillReportsTheTotal()
    {
        var (client, _, _) = await SignInAsync();
        await CreateListsAsync(client, _listCount);

        var page = await ReadPageAsync(client, page: 99, pageSize: 2);

        page.Items.ShouldBeEmpty();
        page.TotalCount.ShouldBe(_listCount);
    }

    // 101 is TodoListCollectionPolicy.Instance.MaxPageSize + 1, restated as a literal: InlineData
    // arguments must be compile-time constants, so the arithmetic cannot reference the policy
    // directly. TodoListCollectionPolicyTests asserts the ceiling is 100, which is what keeps this
    // literal honest.
    [Theory]
    [InlineData(0, 20, "the page number must be 1 or greater")]
    [InlineData(-1, 20, "a negative page number")]
    [InlineData(1, 0, "a page size of zero")]
    [InlineData(1, -5, "a negative page size")]
    [InlineData(1, 101, "a page size above the ceiling")]
    public async Task AnOutOfBoundsPageRequest_Is400WithTheStableCode(int page, int pageSize, string why)
    {
        var (client, _, _) = await SignInAsync();

        using var response = await client.GetAsync(
            new Uri(
                string.Create(CultureInfo.InvariantCulture, $"{TodoListsRoute}?page={page}&pageSize={pageSize}"),
                UriKind.Relative),
            TestToken);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest, why);
        (await ApiJson.ReadProblemAsync(response, TestToken)).Code.ShouldBe("paging.invalid", why);
    }

    [Fact]
    public async Task ThePageSizeCeiling_IsItselfAccepted()
    {
        // The bound is inclusive; without this the test above would also pass for an off-by-one that
        // rejected the largest legal page.
        var (client, _, _) = await SignInAsync();

        var page = await ReadPageAsync(client, page: 1, pageSize: TodoListCollectionPolicy.Instance.MaxPageSize);

        page.PageSize.ShouldBe(TodoListCollectionPolicy.Instance.MaxPageSize);
    }

    [Fact]
    public async Task TheDefaults_AreAppliedWhenNothingIsAsked()
    {
        var (client, _, _) = await SignInAsync();
        await CreateListsAsync(client, _listCount);

        using var response = await client.GetAsync(new Uri(TodoListsRoute, UriKind.Relative), TestToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var page = await ApiJson.ReadAsync<PagedResult<TodoListSummaryDto>>(response, TestToken);
        page.Page.ShouldBe(1);
        page.PageSize.ShouldBe(20);
        page.Items.Count.ShouldBe(_listCount);
    }

    [Fact]
    public async Task TheSummary_CountsItemsAndCompletedItems()
    {
        var (client, _, _) = await SignInAsync();
        var listId = await CreateTodoListAsync(client, "Counted");
        var first = await AddTodoItemAsync(client, listId, "First");
        await AddTodoItemAsync(client, listId, "Second");

        using var completed = await client.PostAsync(
            new Uri($"{TodoListsRoute}/{listId}/items/{first}/complete", UriKind.Relative),
            content: null,
            TestToken);
        completed.EnsureSuccessStatusCode();

        var summary = (await ReadPageAsync(client, page: 1, pageSize: 20)).Items.Single();

        summary.ItemCount.ShouldBe(2);
        summary.CompletedItemCount.ShouldBe(1);
    }

    private static async Task CreateListsAsync(HttpClient client, int count)
    {
        for (int index = 1; index <= count; index++)
        {
            await CreateTodoListAsync(client, $"List {index}");
        }
    }

    private static async Task<PagedResult<TodoListSummaryDto>> ReadPageAsync(HttpClient client, int page, int pageSize)
    {
        using var response = await client.GetAsync(
            new Uri(
                string.Create(CultureInfo.InvariantCulture, $"{TodoListsRoute}?page={page}&pageSize={pageSize}"),
                UriKind.Relative),
            TestToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        return await ApiJson.ReadAsync<PagedResult<TodoListSummaryDto>>(response, TestToken);
    }
}

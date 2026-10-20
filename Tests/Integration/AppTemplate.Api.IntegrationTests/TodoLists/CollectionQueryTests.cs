using System.Globalization;
using System.Net;
using AppTemplate.Api.Common.Contracts;
using AppTemplate.Api.Features.TodoLists.Contracts.Responses;
using AppTemplate.Api.IntegrationTests.Infrastructure;
using AppTemplate.Application.Core.Common.Collections;
using Shouldly;
using Xunit;

namespace AppTemplate.Api.IntegrationTests.TodoLists;

public sealed class CollectionQueryTests(ApiFixture fixture) : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task Sort_NameAscending_OrdersByName()
    {
        var (client, _, _) = await SignInAsync();
        await CreateTodoListAsync(client, "Charlie");
        await CreateTodoListAsync(client, "Alpha");
        await CreateTodoListAsync(client, "Bravo");

        var page = await ReadPageAsync(client, "sort=name:asc");

        page.Items.Select(item => item.Name).ShouldBe(["Alpha", "Bravo", "Charlie"]);
    }

    [Fact]
    public async Task Sort_NameDescending_OrdersByName()
    {
        var (client, _, _) = await SignInAsync();
        await CreateTodoListAsync(client, "Charlie");
        await CreateTodoListAsync(client, "Alpha");
        await CreateTodoListAsync(client, "Bravo");

        var page = await ReadPageAsync(client, "sort=name:desc");

        page.Items.Select(item => item.Name).ShouldBe(["Charlie", "Bravo", "Alpha"]);
    }

    [Fact]
    public async Task Sort_MultiTerm_CreatedAtThenName_BreaksTiesByName()
    {
        var (client, _, _) = await SignInAsync();

        await CreateTodoListAsync(client, "Charlie");
        await CreateTodoListAsync(client, "Alpha");
        await CreateTodoListAsync(client, "Bravo");

        var page = await ReadPageAsync(client, "sort=createdAt:asc,name:asc");

        page.Items.Select(item => item.Name).ShouldBe(["Alpha", "Bravo", "Charlie"]);
    }

    [Fact]
    public async Task Sort_AnUnknownField_Is400WithTheStableCode()
    {
        var (client, _, _) = await SignInAsync();

        using var response = await GetAsync(client, "sort=nope");

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ApiJson.ReadProblemAsync(response, TestToken)).Code.ShouldBe("sort.invalid");
    }

    [Fact]
    public async Task Sort_AFieldThatExistsButIsNotWhitelisted_Is400WithTheStableCode()
    {
        var (client, _, _) = await SignInAsync();

        using var response = await GetAsync(client, "sort=ownerId");

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ApiJson.ReadProblemAsync(response, TestToken)).Code.ShouldBe("sort.invalid");
    }

    [Fact]
    public async Task Sort_ADuplicateField_Is400WithTheStableCode()
    {
        var (client, _, _) = await SignInAsync();

        using var response = await GetAsync(client, "sort=name,name");

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ApiJson.ReadProblemAsync(response, TestToken)).Code.ShouldBe("sort.invalid");
    }

    [Fact]
    public async Task Sort_MoreTermsThanTheCeiling_Is400WithTheStableCode()
    {
        var (client, _, _) = await SignInAsync();

        using var response = await GetAsync(client, "sort=name:asc,createdAt:asc,lastModifiedAt:asc,name:desc");

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ApiJson.ReadProblemAsync(response, TestToken)).Code.ShouldBe("sort.invalid");
    }

    [Fact]
    public async Task Sort_ExactlyTheCeiling_IsAccepted()
    {
        var (client, _, _) = await SignInAsync();
        await CreateTodoListAsync(client, "Only list");

        using var response = await GetAsync(client, "sort=name:asc,createdAt:asc,lastModifiedAt:asc");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Sort_ABadDirection_Is400WithTheStableCode()
    {
        var (client, _, _) = await SignInAsync();

        using var response = await GetAsync(client, "sort=name:sideways");

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ApiJson.ReadProblemAsync(response, TestToken)).Code.ShouldBe("sort.invalid");
    }

    [Fact]
    public async Task Sort_AStableTiebreaker_CoversEveryRowExactlyOnceAcrossOffsetPages()
    {
        var (client, _, _) = await SignInAsync();

        const int total = 7;
        for (int index = 1; index <= total; index++)
        {
            await CreateTodoListAsync(client, $"List {index}");
        }

        var seen = new List<Guid>();

        for (int page = 1; page <= 4; page++)
        {
            var result = await ReadPageAsync(client, $"sort=createdAt:asc&page={page}&pageSize=2");
            seen.AddRange(result.Items.Select(item => item.Id));
        }

        seen.Count.ShouldBe(total);
        seen.Distinct().Count().ShouldBe(total);
    }

    [Fact]
    public async Task Search_MatchesASubstringOfTheName_CaseInsensitively()
    {
        var (client, _, _) = await SignInAsync();
        await CreateTodoListAsync(client, "Groceries");
        await CreateTodoListAsync(client, "Reading list");

        var lower = await ReadPageAsync(client, "search=grocer");
        var upper = await ReadPageAsync(client, "search=GROCER");

        lower.Items.Select(item => item.Name).ShouldBe(["Groceries"]);
        upper.Items.Select(item => item.Name).ShouldBe(["Groceries"]);
    }

    [Fact]
    public async Task Search_DoesNotMatchAnotherUsersLists()
    {
        var (owner, _, _) = await SignInAsync();
        await CreateTodoListAsync(owner, "Shared name");

        var (other, _, _) = await SignInAsync();
        await CreateTodoListAsync(other, "Shared name");

        var page = await ReadPageAsync(other, "search=shared");

        page.Items.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Search_EscapesThePercentWildcard()
    {
        var (client, _, _) = await SignInAsync();
        await CreateTodoListAsync(client, "100% cotton");
        await CreateTodoListAsync(client, "Plain");

        var page = await ReadPageAsync(client, $"search={Uri.EscapeDataString("%")}");

        page.Items.Select(item => item.Name).ShouldBe(["100% cotton"]);
    }

    [Fact]
    public async Task Search_EscapesTheUnderscoreWildcard()
    {
        var (client, _, _) = await SignInAsync();
        await CreateTodoListAsync(client, "a_b");
        await CreateTodoListAsync(client, "axb");

        var page = await ReadPageAsync(client, "search=a_b");

        page.Items.Select(item => item.Name).ShouldBe(["a_b"]);
    }

    [Fact]
    public async Task Search_AtExactlyTheMaxLength_IsAccepted()
    {
        var (client, _, _) = await SignInAsync();
        string search = new('a', SearchTerm.MaxLength);

        using var response = await GetAsync(client, $"search={search}");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Search_OneCharacterPastTheMaxLength_Is400WithTheStableCode()
    {
        var (client, _, _) = await SignInAsync();
        string search = new('a', SearchTerm.MaxLength + 1);

        using var response = await GetAsync(client, $"search={search}");

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ApiJson.ReadProblemAsync(response, TestToken)).Code.ShouldBe("filter.invalid");
    }

    [Fact]
    public async Task CreatedAfterAndCreatedBefore_NarrowInclusivelyOnBothEnds()
    {
        var (client, _, _) = await SignInAsync();

        var early = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var middle = new DateTimeOffset(2026, 1, 2, 0, 0, 0, TimeSpan.Zero);
        var late = new DateTimeOffset(2026, 1, 3, 0, 0, 0, TimeSpan.Zero);

        Clock.Set(early);
        var earlyId = await CreateTodoListAsync(client, "Early");

        Clock.Set(middle);
        var middleId = await CreateTodoListAsync(client, "Middle");

        Clock.Set(late);
        await CreateTodoListAsync(client, "Late");

        string after = Iso(early);
        string before = Iso(middle);

        var page = await ReadPageAsync(
            client,
            $"createdAfter={Uri.EscapeDataString(after)}&createdBefore={Uri.EscapeDataString(before)}");

        page.Items.Select(item => item.Id).ShouldBe([earlyId, middleId], ignoreOrder: true);
    }

    [Fact]
    public async Task CreatedAfterLaterThanCreatedBefore_Is400WithTheStableCode()
    {
        var (client, _, _) = await SignInAsync();

        using var response = await GetAsync(
            client,
            $"createdAfter={Uri.EscapeDataString("2026-01-02T00:00:00Z")}"
            + $"&createdBefore={Uri.EscapeDataString("2026-01-01T00:00:00Z")}");

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ApiJson.ReadProblemAsync(response, TestToken)).Code.ShouldBe("filter.invalid");
    }

    [Fact]
    public async Task AnUnparseableCreatedAfter_Is400WithTheStableCode()
    {
        var (client, _, _) = await SignInAsync();

        using var response = await GetAsync(client, "createdAfter=not-a-date");

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ApiJson.ReadProblemAsync(response, TestToken)).Code.ShouldBe("filter.invalid");
    }

    [Fact]
    public async Task AnUnknownQueryParameter_CannotFilterAnything()
    {
        var (client, _, _) = await SignInAsync();
        await CreateTodoListAsync(client, "First");
        await CreateTodoListAsync(client, "Second");

        var unfiltered = await ReadPageAsync(client, "");
        var withUnknownParam = await ReadPageAsync(client, $"ownerId={Guid.CreateVersion7()}");

        withUnknownParam.Items.Select(item => item.Id)
            .ShouldBe(unfiltered.Items.Select(item => item.Id), ignoreOrder: true);
    }

    [Fact]
    public async Task Search_IsNotAccentInsensitive()
    {
        var (client, _, _) = await SignInAsync();
        await CreateTodoListAsync(client, "Café");

        var page = await ReadPageAsync(client, "search=Cafe");

        page.Items.ShouldBeEmpty();
    }

    private static string Iso(DateTimeOffset instant) => instant.ToString("O", CultureInfo.InvariantCulture);

    private static async Task<HttpResponseMessage> GetAsync(HttpClient client, string query) =>
        await client.GetAsync(new Uri($"{TodoListsRoute}?{query}", UriKind.Relative), TestToken);

    private static async Task<PagedResponse<TodoListSummaryResponse>> ReadPageAsync(HttpClient client, string query)
    {
        using var response = await GetAsync(client, query);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        return await ApiJson.ReadAsync<PagedResponse<TodoListSummaryResponse>>(response, TestToken);
    }
}

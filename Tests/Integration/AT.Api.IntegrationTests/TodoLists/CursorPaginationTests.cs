using System.Net;
using System.Text;
using AppTemplate.Api.Common.Contracts;
using AppTemplate.Api.Features.TodoLists.Contracts.Responses;
using AppTemplate.Api.IntegrationTests.Infrastructure;
using AppTemplate.Application.Core.Common.Collections;
using AppTemplate.Application.Features.TodoLists.Model.Policies;
using Shouldly;
using Xunit;

namespace AppTemplate.Api.IntegrationTests.TodoLists;

public sealed class CursorPaginationTests(ApiFixture fixture) : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task AFullWalk_UnderNameAscending_CoversEveryRowExactlyOnce()
    {
        var (client, _, _) = await SignInAsync();

        var ids = new List<Guid>();
        for (int index = 1; index <= 7; index++)
        {
            ids.Add(await CreateTodoListAsync(client, $"List {index:D2}"));
        }

        var seen = await WalkAsync(client, "sort=name:asc&pageSize=2");

        seen.Count.ShouldBe(ids.Count);
        seen.Distinct().Count().ShouldBe(ids.Count);
        seen.ShouldBe(ids, ignoreOrder: true);
    }

    [Fact]
    public async Task AFullWalk_UnderTheDefaultSort_CoversEveryRowExactlyOnce()
    {
        var (client, _, _) = await SignInAsync();

        var ids = new List<Guid>();
        for (int index = 1; index <= 7; index++)
        {
            ids.Add(await CreateTodoListAsync(client, $"List {index}"));
        }

        var seen = await WalkAsync(client, "pageSize=2");

        seen.Count.ShouldBe(ids.Count);
        seen.Distinct().Count().ShouldBe(ids.Count);
    }

    [Fact]
    public async Task TheLastPage_HasANullNextCursor_AndOffsetFieldsStayNull()
    {
        var (client, _, _) = await SignInAsync();
        await CreateTodoListAsync(client, "Only list");

        var page = await ReadCursorPageAsync(client, "pageSize=20");

        page.NextCursor.ShouldBeNull();
        page.TotalCount.ShouldBeNull();
        page.Page.ShouldBeNull();
        page.TotalPages.ShouldBeNull();
        page.PageSize.ShouldBe(20);
    }

    [Fact]
    public async Task ACursorPage_IsNotDisturbedByARowInsertedBeforeItsPosition()
    {
        var (client, _, _) = await SignInAsync();

        var ids = new List<Guid>
        {
            await CreateTodoListAsync(client, "Bravo"),
            await CreateTodoListAsync(client, "Delta"),
            await CreateTodoListAsync(client, "Foxtrot"),
            await CreateTodoListAsync(client, "Hotel"),
        };

        var first = await ReadCursorPageAsync(client, "sort=name:asc&pageSize=2");
        first.Items.Select(item => item.Name).ShouldBe(["Bravo", "Delta"]);

        var insertedId = await CreateTodoListAsync(client, "Alpha");

        var second = await ReadCursorPageAsync(
            client,
            $"sort=name:asc&pageSize=2&cursor={Uri.EscapeDataString(first.NextCursor!)}");

        var page1Ids = first.Items.Select(item => item.Id).ToList();
        var page2Ids = second.Items.Select(item => item.Id).ToList();

        page2Ids.ShouldNotContain(id => page1Ids.Contains(id));
        page2Ids.ShouldNotContain(insertedId);
        page2Ids.ShouldBe([ids[2], ids[3]], ignoreOrder: true);
    }

    [Fact]
    public async Task TheOffsetEquivalent_RepeatsARowWhenARowIsInsertedBeforeItsPosition()
    {
        var (client, _, _) = await SignInAsync();

        var ids = new List<Guid>
        {
            await CreateTodoListAsync(client, "Bravo"),
            await CreateTodoListAsync(client, "Delta"),
            await CreateTodoListAsync(client, "Foxtrot"),
            await CreateTodoListAsync(client, "Hotel"),
        };

        var first = await ReadOffsetPageAsync(client, "sort=name:asc&page=1&pageSize=2");
        first.Items.Select(item => item.Name).ShouldBe(["Bravo", "Delta"]);

        await CreateTodoListAsync(client, "Alpha");

        var second = await ReadOffsetPageAsync(client, "sort=name:asc&page=2&pageSize=2");

        second.Items.Select(item => item.Id).ShouldContain(ids[1]);
    }

    [Fact]
    public async Task AMultiTermSort_UnderCursorPaging_Is400CursorInvalid()
    {
        var (client, _, _) = await SignInAsync();
        await CreateTodoListAsync(client, "Only list");

        using var response = await GetAsync(client, "paging=cursor&sort=name:asc,createdAt:asc");

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ApiJson.ReadProblemAsync(response, TestToken)).Code.ShouldBe("cursor.invalid");
    }

    [Fact]
    public async Task CursorPaging_WithAPageNumber_Is400PagingInvalid()
    {
        var (client, _, _) = await SignInAsync();
        await CreateTodoListAsync(client, "Only list");

        using var response = await GetAsync(client, "paging=cursor&page=2");

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ApiJson.ReadProblemAsync(response, TestToken)).Code.ShouldBe("paging.invalid");
    }

    [Fact]
    public async Task ACursor_SentWithOffsetPaging_Is400PagingInvalid()
    {
        var (client, _, _) = await SignInAsync();
        await CreateTodoListAsync(client, "First");
        await CreateTodoListAsync(client, "Second");

        var minted = await ReadCursorPageAsync(client, "sort=name:asc&pageSize=1");
        minted.NextCursor.ShouldNotBeNull();

        using var response = await GetAsync(
            client,
            $"paging=offset&cursor={Uri.EscapeDataString(minted.NextCursor!)}");

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ApiJson.ReadProblemAsync(response, TestToken)).Code.ShouldBe("paging.invalid");
    }

    [Fact]
    public async Task ACursorOverAFieldNotKeysetCapable_Is400CursorInvalid()
    {
        var (client, _, _) = await SignInAsync();
        await CreateTodoListAsync(client, "Only list");

        var term = SortOrder.Parse(TodoListCollectionPolicy.LastModifiedAtField, TodoListCollectionPolicy.Instance)
            .Value.Terms[0];
        string cursor = Cursor.After(term, DateTimeOffset.UtcNow.ToString("O"), Guid.CreateVersion7()).Encode();

        using var response = await GetAsync(client, $"paging=cursor&cursor={Uri.EscapeDataString(cursor)}");

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ApiJson.ReadProblemAsync(response, TestToken)).Code.ShouldBe("cursor.invalid");
    }

    [Fact]
    public async Task ACursor_MintedUnderOneField_ReplayedUnderAnother_Is400CursorInvalid()
    {
        var (client, _, _) = await SignInAsync();
        await CreateTodoListAsync(client, "First");
        await CreateTodoListAsync(client, "Second");

        var minted = await ReadCursorPageAsync(client, "sort=name:asc&pageSize=1");
        minted.NextCursor.ShouldNotBeNull();

        using var response = await GetAsync(
            client,
            $"paging=cursor&sort=createdAt:desc&cursor={Uri.EscapeDataString(minted.NextCursor!)}");

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        response.StatusCode.ShouldNotBe(HttpStatusCode.InternalServerError);
        (await ApiJson.ReadProblemAsync(response, TestToken)).Code.ShouldBe("cursor.invalid");
    }

    [Fact]
    public async Task ACursor_MintedUnderOneDirection_ReplayedUnderAnother_Is400CursorInvalid()
    {
        var (client, _, _) = await SignInAsync();
        await CreateTodoListAsync(client, "First");
        await CreateTodoListAsync(client, "Second");

        var minted = await ReadCursorPageAsync(client, "sort=name:asc&pageSize=1");
        minted.NextCursor.ShouldNotBeNull();

        using var response = await GetAsync(
            client,
            $"paging=cursor&sort=name:desc&cursor={Uri.EscapeDataString(minted.NextCursor!)}");

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        response.StatusCode.ShouldNotBe(HttpStatusCode.InternalServerError);
        (await ApiJson.ReadProblemAsync(response, TestToken)).Code.ShouldBe("cursor.invalid");
    }

    public static TheoryData<string> AdversarialCursors()
    {
        return
        [
            "!!!!",
            "a",
            "////",
            Base64UrlOf("not json"),
            Base64UrlOf("{}"),
            new string('a', 600),
        ];
    }

    [Fact]
    public async Task ACursorSentAsAnEmptyString_IsTreatedAsNoCursorAndServesTheFirstPage()
    {
        var (client, _, _) = await SignInAsync();
        await CreateTodoListAsync(client, "Only list");

        using var response = await GetAsync(client, "paging=cursor&cursor=");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var page = await ApiJson.ReadAsync<PagedResponse<TodoListSummaryResponse>>(response, TestToken);
        page.Items.Count.ShouldBe(1);
    }

    [Theory]
    [MemberData(nameof(AdversarialCursors))]
    public async Task ATamperedCursor_Is400AndNeverA500(string cursor)
    {
        var (client, _, _) = await SignInAsync();
        await CreateTodoListAsync(client, "Only list");

        using var response = await GetAsync(client, $"paging=cursor&cursor={Uri.EscapeDataString(cursor)}");

        response.StatusCode.ShouldNotBe(HttpStatusCode.InternalServerError, cursor);
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest, cursor);

        string code = (await ApiJson.ReadProblemAsync(response, TestToken)).Code!;
        code.ShouldBeOneOf("cursor.invalid", "paging.invalid");
    }

    [Fact]
    public async Task AGenuineCursor_WithOneCharacterOfItsPayloadFlipped_Is400AndNeverA500()
    {
        var (client, _, _) = await SignInAsync();
        await CreateTodoListAsync(client, "First");
        await CreateTodoListAsync(client, "Second");

        var minted = await ReadCursorPageAsync(client, "sort=name:asc&pageSize=1");
        string genuine = minted.NextCursor!;

        int middle = genuine.Length / 2;
        char original = genuine[middle];
        char flipped = original == 'A' ? 'B' : 'A';
        string tampered = string.Concat(genuine.AsSpan(0, middle), flipped.ToString(), genuine.AsSpan(middle + 1));

        using var response = await GetAsync(client, $"paging=cursor&sort=name:asc&cursor={Uri.EscapeDataString(tampered)}");

        response.StatusCode.ShouldNotBe(HttpStatusCode.InternalServerError);
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        string code = (await ApiJson.ReadProblemAsync(response, TestToken)).Code!;
        code.ShouldBeOneOf("cursor.invalid", "paging.invalid");
    }

    private static string Base64UrlOf(string text) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(text)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static async Task<List<Guid>> WalkAsync(HttpClient client, string query)
    {
        var seen = new List<Guid>();
        string? cursor = null;

        do
        {
            string pageQuery = cursor is null
                ? query
                : $"{query}&cursor={Uri.EscapeDataString(cursor)}";

            var page = await ReadCursorPageAsync(client, pageQuery);
            seen.AddRange(page.Items.Select(item => item.Id));
            cursor = page.NextCursor;
        }
        while (cursor is not null);

        return seen;
    }

    private static async Task<HttpResponseMessage> GetAsync(HttpClient client, string query) =>
        await client.GetAsync(new Uri($"{TodoListsRoute}?{query}", UriKind.Relative), TestToken);

    private static async Task<PagedResponse<TodoListSummaryResponse>> ReadCursorPageAsync(HttpClient client, string query)
    {
        using var response = await GetAsync(client, $"paging=cursor&{query}");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        return await ApiJson.ReadAsync<PagedResponse<TodoListSummaryResponse>>(response, TestToken);
    }

    private static async Task<PagedResponse<TodoListSummaryResponse>> ReadOffsetPageAsync(HttpClient client, string query)
    {
        using var response = await GetAsync(client, query);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        return await ApiJson.ReadAsync<PagedResponse<TodoListSummaryResponse>>(response, TestToken);
    }
}

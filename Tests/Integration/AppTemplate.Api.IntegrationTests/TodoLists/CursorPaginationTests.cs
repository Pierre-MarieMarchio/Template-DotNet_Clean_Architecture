using System.Net;
using System.Text;
using AppTemplate.Api.Common.Contracts;
using AppTemplate.Api.Features.TodoLists.Contracts.Responses;
using AppTemplate.Api.IntegrationTests.Infrastructure;
using AppTemplate.Application.Common.Collections;
using AppTemplate.Application.Features.TodoLists.Policies;
using Shouldly;
using Xunit;

namespace AppTemplate.Api.IntegrationTests.TodoLists;

/// <summary>
/// <c>paging=cursor</c>: the keyset walk, the guarantees it makes that offset paging does not, and
/// every way a cursor can be malformed, foreign, or stale.
/// </summary>
public sealed class CursorPaginationTests(ApiFixture fixture) : IntegrationTestBase(fixture)
{
    #region The walk

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

    #endregion

    #region Paging stability

    /// <summary>
    /// The point of keyset paging: a row inserted between two reads, sorting before the cursor's
    /// position, must not disturb what the next page serves.
    /// </summary>
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

        // Sorts before every row served so far under name:asc.
        var insertedId = await CreateTodoListAsync(client, "Alpha");

        var second = await ReadCursorPageAsync(
            client,
            $"sort=name:asc&pageSize=2&cursor={Uri.EscapeDataString(first.NextCursor!)}");

        var page1Ids = first.Items.Select(item => item.Id).ToList();
        var page2Ids = second.Items.Select(item => item.Id).ToList();

        // Neither repeated nor skipped, and the row that sorts before the cursor's own position
        // never appears on the resumed page: keyset paging resumes strictly after the last row it
        // already served, regardless of what was inserted behind that point.
        page2Ids.ShouldNotContain(id => page1Ids.Contains(id));
        page2Ids.ShouldNotContain(insertedId);
        page2Ids.ShouldBe([ids[2], ids[3]], ignoreOrder: true);
    }

    /// <summary>
    /// The contrast, demonstrated rather than assumed: the same shape of insertion under offset
    /// paging shifts every row's position, so the second page repeats a row the first page already
    /// served.
    /// </summary>
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

        // Sorts before every existing row under name:asc, so it becomes the new position 1.
        await CreateTodoListAsync(client, "Alpha");

        var second = await ReadOffsetPageAsync(client, "sort=name:asc&page=2&pageSize=2");

        // "Delta" (ids[1]) was the last row of page 1; the insertion pushed it back onto page 2,
        // so it is now served a second time. This is exactly the instability keyset paging exists to
        // avoid — offset paging is not stable under concurrent inserts.
        second.Items.Select(item => item.Id).ShouldContain(ids[1]);
    }

    #endregion

    #region Mode and sort combinations

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

    /// <summary>
    /// <c>lastModifiedAt</c> is whitelisted for sorting but is nullable, so it cannot be resumed from
    /// under a keyset comparison: a comparison against <c>NULL</c> is neither true nor false.
    /// </summary>
    /// <remarks>
    /// Production never mints a cursor over <c>lastModifiedAt</c> — the read side only calls
    /// <c>Cursor.After</c> once a page has actually been served under a keyset-capable field — so the
    /// only way to drive this refusal is to build one by hand, the same way a caller who inspected the
    /// wire format could. <c>SortOrder.Parse</c> and <c>Cursor.After</c>/<c>Encode</c> are both public
    /// on the Application assembly this project already references, so this needs no internals.
    /// </remarks>
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

    /// <summary>
    /// A cursor minted under one field, replayed under a different one: comparing the cursor's key as
    /// a value of the new field's type would otherwise be the persistence layer's problem, and its
    /// only recourse there is to throw — a 500 for what is really a malformed request.
    /// </summary>
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

    /// <summary>The direction-only variant of the mismatch above.</summary>
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

    #endregion

    #region Tampered cursors

    /// <remarks>
    /// An empty string is deliberately not one of these. This codebase treats a blank value as "not
    /// supplied" consistently — a blank <c>search</c>, a blank <c>sort</c> — and <c>cursor</c> follows
    /// the same rule (<c>GetTodoListsUseCase</c> only decodes when
    /// <c>!string.IsNullOrWhiteSpace(query.Cursor)</c>). An empty <c>cursor</c> is therefore a
    /// legitimate request for the first page, not tampering; that behaviour is pinned on its own below
    /// rather than asserted here as if it were malformed.
    /// </remarks>
    public static TheoryData<string> AdversarialCursors()
    {
        // A cursor with one payload character flipped is generated per-run in the test itself,
        // because it needs a real cursor minted by this test's own data; every value here is one a
        // caller could send with no cursor ever having existed.
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

    /// <summary>The other half of the note above, asserted rather than assumed.</summary>
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

        // Flips one character in the middle of the payload rather than the first or last, so the
        // result is still plausible Base64Url of roughly the right length.
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

    #endregion

    private static string Base64UrlOf(string text) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(text)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    /// <summary>Walks every cursor page from the start, following <c>nextCursor</c> until it is null.</summary>
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

    /// <summary>
    /// Reads a page in cursor mode. <paramref name="query"/> carries only what varies per call — the
    /// sort, the page size, an optional cursor — never <c>paging=cursor</c> itself, so it is never
    /// duplicated.
    /// </summary>
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

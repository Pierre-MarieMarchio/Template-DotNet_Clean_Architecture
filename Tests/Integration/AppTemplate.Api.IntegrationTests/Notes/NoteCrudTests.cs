using System.Net;
using System.Net.Http.Json;
using AppTemplate.Api.Common.Contracts;
using AppTemplate.Api.Features.Notes.Contracts.Requests;
using AppTemplate.Api.Features.Notes.Contracts.Responses;
using AppTemplate.Api.Features.Notes.Controllers;
using AppTemplate.Api.IntegrationTests.Infrastructure;
using Shouldly;
using Xunit;

namespace AppTemplate.Api.IntegrationTests.Notes;

/// <summary>
/// One user creates, lists, reads, edits and deletes their notes through the real API and the real
/// database, and every route refuses an anonymous caller.
/// </summary>
public sealed class NoteCrudTests(ApiFixture fixture) : NotesTestBase(fixture)
{
    [Fact]
    public async Task ANote_CanBeCreated_Read_Updated_ListedAndDeleted()
    {
        var (client, _, _) = await SignInAsync();

        var created = await CreateNoteAsync(client, "Groceries", "Milk");
        created.Note.Title.ShouldBe("Groceries");
        created.Note.Body.ShouldBe("Milk");
        created.Note.LastModifiedAt.ShouldBeNull();

        var read = await ReadNoteAsync(client, created.Note.Id);
        read.Note.ShouldBe(created.Note);
        read.ETag.ShouldBe(created.ETag);

        using var updated = await UpdateNoteAsync(client, created.Note.Id, "Groceries (done)", "Everything", created.ETag);
        var afterUpdate = await ReadWrittenNoteAsync(updated, HttpStatusCode.OK);
        afterUpdate.Note.Title.ShouldBe("Groceries (done)");
        afterUpdate.Note.LastModifiedAt.ShouldNotBeNull();
        afterUpdate.ETag.ShouldNotBe(created.ETag, "a write moves the version, and the response carries the new one");

        var page = await GetPageAsync(client);
        page.Items.ShouldHaveSingleItem().Title.ShouldBe("Groceries (done)");

        using var deleted = await DeleteNoteAsync(client, created.Note.Id, afterUpdate.ETag);
        deleted.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        using var gone = await client.GetAsync(new Uri($"{NotesRoute}/{created.Note.Id}", UriKind.Relative), TestToken);
        gone.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await ApiJson.ReadProblemAsync(gone, TestToken)).Code.ShouldBe("note.notFound");
    }

    [Fact]
    public async Task Creating_AnswersALocation_ThatLeadsToTheNote()
    {
        var (client, _, _) = await SignInAsync();

        using var response = await client.PostAsJsonAsync(NotesRoute, new CreateNoteRequest("Groceries", "Milk"), TestToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var note = await ApiJson.ReadAsync<NoteResponse>(response, TestToken);
        response.Headers.Location.ShouldNotBeNull().ToString().ShouldEndWith($"{NotesRoute}/{note.Id}");

        using var followed = await client.GetAsync(response.Headers.Location, TestToken);
        followed.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    /// <summary>The owner is the caller. A body that names one is not a field, so it cannot choose it.</summary>
    [Fact]
    public async Task TheOwner_CannotBeChosenInTheBody()
    {
        var (owner, _, _) = await SignInAsync("owner");
        var (other, _, otherSession) = await SignInAsync("other");

        using var response = await owner.PostAsJsonAsync(
            NotesRoute,
            new { title = "Mine", body = "Mine", ownerId = otherSession.UserId },
            TestToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        (await GetPageAsync(owner)).Items.Count.ShouldBe(1);
        (await GetPageAsync(other)).Items.ShouldBeEmpty();
    }

    [Fact]
    public async Task TheTitleIsTrimmed_AndAnEmptyBodyIsAccepted()
    {
        var (client, _, _) = await SignInAsync();

        var created = await CreateNoteAsync(client, "  Padded  ", string.Empty);

        created.Note.Title.ShouldBe("Padded");
        created.Note.Body.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("", "body")]
    [InlineData("   ", "body")]
    public async Task ABlankTitle_Answers400(string title, string body)
    {
        var (client, _, _) = await SignInAsync();

        using var response = await client.PostAsJsonAsync(NotesRoute, new CreateNoteRequest(title, body), TestToken);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ApiJson.ReadProblemAsync(response, TestToken)).Code.ShouldBe("request.validationFailed");
        (await GetPageAsync(client)).Items.ShouldBeEmpty();
    }

    [Fact]
    public async Task ABodyWithoutATitleOrBody_Answers400_NotA500()
    {
        var (client, _, _) = await SignInAsync();

        using var response = await client.PostAsJsonAsync(NotesRoute, new { }, TestToken);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ApiJson.ReadProblemAsync(response, TestToken)).Code.ShouldBe("request.validationFailed");
    }

    [Fact]
    public async Task ATitleOverTheLimit_Answers400()
    {
        var (client, _, _) = await SignInAsync();

        using var response = await client.PostAsJsonAsync(
            NotesRoute,
            new CreateNoteRequest(new string('a', 201), "body"),
            TestToken);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task AnUpdateWithAnInvalidTitle_Answers400_AndChangesNothing()
    {
        var (client, _, _) = await SignInAsync();
        var created = await CreateNoteAsync(client, "Groceries");

        using var response = await UpdateNoteAsync(client, created.Note.Id, " ", "body", created.ETag);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ReadNoteAsync(client, created.Note.Id)).Note.Title.ShouldBe("Groceries");
    }

    [Fact]
    public async Task TheList_IsPaged_AndSortable_InBothModes()
    {
        var (client, _, _) = await SignInAsync();

        foreach (string title in new[] { "Charlie", "Alpha", "Bravo" })
        {
            await CreateNoteAsync(client, title);
        }

        var offset = await GetPageAsync(client, "?sort=title:asc&pageSize=2");
        offset.Items.Select(item => item.Title).ShouldBe(["Alpha", "Bravo"]);
        offset.TotalCount.ShouldBe(3);
        offset.HasNextPage.ShouldBeTrue();

        var first = await GetPageAsync(client, "?paging=cursor&sort=title:asc&pageSize=2");
        first.Items.Select(item => item.Title).ShouldBe(["Alpha", "Bravo"]);
        first.NextCursor.ShouldNotBeNull();

        var second = await GetPageAsync(client, $"?paging=cursor&sort=title:asc&pageSize=2&cursor={Uri.EscapeDataString(first.NextCursor)}");
        second.Items.Select(item => item.Title).ShouldBe(["Charlie"]);
        second.NextCursor.ShouldBeNull();
    }

    [Fact]
    public async Task ASortOutsideTheWhitelist_Answers400()
    {
        var (client, _, _) = await SignInAsync();

        using var response = await client.GetAsync(new Uri($"{NotesRoute}?sort=body:asc", UriKind.Relative), TestToken);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ApiJson.ReadProblemAsync(response, TestToken)).Code.ShouldBe("sort.invalid");
    }

    [Fact]
    public async Task AReadWithTheCurrentETag_Answers304()
    {
        var (client, _, _) = await SignInAsync();
        var created = await CreateNoteAsync(client);

        using var request = new HttpRequestMessage(HttpMethod.Get, $"{NotesRoute}/{created.Note.Id}");
        request.Headers.TryAddWithoutValidation("If-None-Match", created.ETag);

        using var response = await client.SendAsync(request, TestToken);

        response.StatusCode.ShouldBe(HttpStatusCode.NotModified);
    }

    /// <summary>
    /// The three-fifths of the surface that has no body to fall back on: every route, with no token and
    /// with a garbage one, answers 401 before anything else is looked at.
    /// </summary>
    [Theory]
    [InlineData("GET", NotesRoute)]
    [InlineData("POST", NotesRoute)]
    [InlineData("GET", $"{NotesRoute}/00000000-0000-0000-0000-000000000001")]
    [InlineData("PUT", $"{NotesRoute}/00000000-0000-0000-0000-000000000001")]
    [InlineData("DELETE", $"{NotesRoute}/00000000-0000-0000-0000-000000000001")]
    public async Task EveryNotesRoute_Answers401_WithoutAValidToken(string method, string path)
    {
        var client = CreateClient();

        foreach (string? token in new string?[] { null, "not-a-token" })
        {
            using var request = new HttpRequestMessage(new HttpMethod(method), path)
            {
                Content = method is "POST" or "PUT"
                    ? new StringContent("{}", System.Text.Encoding.UTF8, "application/json")
                    : null,
            };

            if (token is not null)
            {
                request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {token}");
            }

            using var response = await client.SendAsync(request, TestToken);

            response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        }
    }

    /// <summary>The routes above are only a guard while they are the whole surface.</summary>
    [Fact]
    public void TheControllerHasExactlyTheFiveRoutesTheAnonymousTestCovers() =>
        typeof(NotesController)
            .GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.DeclaredOnly)
            .Where(method => !method.IsSpecialName)
            .Count()
            .ShouldBe(5);

    private static async Task<PagedResponse<NoteSummaryResponse>> GetPageAsync(HttpClient client, string query = "")
    {
        using var response = await client.GetAsync(new Uri($"{NotesRoute}{query}", UriKind.Relative), TestToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(TestToken));

        return await ApiJson.ReadAsync<PagedResponse<NoteSummaryResponse>>(response, TestToken);
    }
}

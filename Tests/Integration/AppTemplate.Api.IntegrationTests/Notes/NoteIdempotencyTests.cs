using System.Net;
using System.Net.Http.Json;
using AppTemplate.Api.Common.Contracts;
using AppTemplate.Api.Features.Notes.Contracts.Requests;
using AppTemplate.Api.Features.Notes.Contracts.Responses;
using AppTemplate.Api.IntegrationTests.Infrastructure;
using Shouldly;
using Xunit;

namespace AppTemplate.Api.IntegrationTests.Notes;

/// <summary>
/// Creating a note is the one write with no resource to name, so a retry is made safe by the optional
/// <c>Idempotency-Key</c>. Update and delete need none: their route names the note and their
/// <c>If-Match</c> makes a repeated request refuse itself.
/// </summary>
public sealed class NoteIdempotencyTests(ApiFixture fixture) : NotesTestBase(fixture)
{
    private const string _headerName = "Idempotency-Key";
    private const string _replayedHeaderName = "Idempotency-Replayed";

    [Fact]
    public async Task TheSameKeyTwice_CreatesOneNote_AndReplaysTheFirstResponse()
    {
        var (client, _, _) = await SignInAsync();

        using var first = await PostAsync(client, "same-key", new CreateNoteRequest("Groceries", "Milk"));
        first.StatusCode.ShouldBe(HttpStatusCode.Created);
        first.Headers.Contains(_replayedHeaderName).ShouldBeFalse("the first request is not a replay");

        var firstNote = await ApiJson.ReadAsync<NoteResponse>(first, TestToken);

        using var second = await PostAsync(client, "same-key", new CreateNoteRequest("Groceries", "Milk"));

        second.StatusCode.ShouldBe(HttpStatusCode.Created);
        (await ApiJson.ReadAsync<NoteResponse>(second, TestToken)).Id.ShouldBe(firstNote.Id);
        second.Headers.Location.ShouldBe(first.Headers.Location);
        second.Headers.ETag.ShouldBe(first.Headers.ETag);
        second.Headers.GetValues(_replayedHeaderName).ShouldContain("true");

        (await CountNotesAsync(client)).ShouldBe(1, "a replay must not create a second note");
    }

    [Fact]
    public async Task TheSameKeyWithADifferentBody_IsRefusedAsKeyReused_AndCreatesNothing()
    {
        var (client, _, _) = await SignInAsync();

        using var first = await PostAsync(client, "reused-key", new CreateNoteRequest("Groceries", "Milk"));
        first.StatusCode.ShouldBe(HttpStatusCode.Created);

        using var second = await PostAsync(client, "reused-key", new CreateNoteRequest("Something else", "Milk"));

        second.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await ApiJson.ReadProblemAsync(second, TestToken)).Code.ShouldBe("idempotency.keyReused");
        (await CountNotesAsync(client)).ShouldBe(1);
    }

    [Fact]
    public async Task WithoutAKey_TwoIdenticalRequests_CreateTwoNotes()
    {
        var (client, _, _) = await SignInAsync();

        await CreateNoteAsync(client);
        await CreateNoteAsync(client);

        (await CountNotesAsync(client)).ShouldBe(2);
    }

    /// <summary>Keys are scoped per user, so two callers choosing the same string do not collide.</summary>
    [Fact]
    public async Task AKeyBelongingToAnotherUser_DoesNotCollide()
    {
        var (owner, _, _) = await SignInAsync("owner");
        var (stranger, _, _) = await SignInAsync("stranger");

        using var ownersNote = await PostAsync(owner, "shared-key", new CreateNoteRequest("Owner's", "Body"));
        using var strangersNote = await PostAsync(stranger, "shared-key", new CreateNoteRequest("Stranger's", "Body"));

        ownersNote.StatusCode.ShouldBe(HttpStatusCode.Created);
        strangersNote.StatusCode.ShouldBe(HttpStatusCode.Created);
        (await ApiJson.ReadAsync<NoteResponse>(ownersNote, TestToken)).Id
            .ShouldNotBe((await ApiJson.ReadAsync<NoteResponse>(strangersNote, TestToken)).Id);
    }

    /// <summary>
    /// The retry of an update, with the <c>If-Match</c> it first carried, refuses itself instead of applying
    /// twice: the first attempt moved the version, so the repeat names one that is gone.
    /// </summary>
    [Fact]
    public async Task ARetriedUpdate_WithTheSameIfMatch_IsRefused_AndAppliesOnce()
    {
        var (client, _, _) = await SignInAsync();
        var note = await CreateNoteAsync(client, "Original");

        using var first = await UpdateNoteAsync(client, note.Note.Id, "Edited", "Body", note.ETag);
        using var retry = await UpdateNoteAsync(client, note.Note.Id, "Edited", "Body", note.ETag);

        first.StatusCode.ShouldBe(HttpStatusCode.OK);
        retry.StatusCode.ShouldBe(HttpStatusCode.PreconditionFailed);
        (await ReadNoteAsync(client, note.Note.Id)).Note.Title.ShouldBe("Edited");
    }

    [Fact]
    public async Task ARetriedDelete_IsNotFound_AfterTheFirstOneApplied()
    {
        var (client, _, _) = await SignInAsync();
        var note = await CreateNoteAsync(client);

        using var first = await DeleteNoteAsync(client, note.Note.Id, note.ETag);
        using var retry = await DeleteNoteAsync(client, note.Note.Id, note.ETag);

        first.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        retry.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    private static Task<HttpResponseMessage> PostAsync(HttpClient client, string idempotencyKey, CreateNoteRequest body)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, NotesRoute) { Content = JsonContent.Create(body) };

        request.Headers.TryAddWithoutValidation(_headerName, idempotencyKey);

        return client.SendAsync(request, TestToken);
    }

    private static async Task<int> CountNotesAsync(HttpClient client)
    {
        using var response = await client.GetAsync(new Uri(NotesRoute, UriKind.Relative), TestToken);

        return (await ApiJson.ReadAsync<PagedResponse<NoteSummaryResponse>>(response, TestToken)).Items.Count;
    }
}

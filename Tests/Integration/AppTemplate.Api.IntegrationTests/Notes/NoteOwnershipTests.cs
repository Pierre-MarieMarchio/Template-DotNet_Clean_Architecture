using System.Net;
using AppTemplate.Api.Common.Contracts;
using AppTemplate.Api.Features.Notes.Contracts.Responses;
using AppTemplate.Api.IntegrationTests.Infrastructure;
using Shouldly;
using Xunit;

namespace AppTemplate.Api.IntegrationTests.Notes;

/// <summary>
/// One user's notes are invisible to another across the HTTP boundary, and "not yours" is
/// indistinguishable from "does not exist": every attempt answers 404, never 403, so no endpoint
/// taking a note id is an oracle for other users' ids.
/// </summary>
public sealed class NoteOwnershipTests(ApiFixture fixture) : NotesTestBase(fixture)
{
    [Fact]
    public async Task AnotherUsersNotes_AreAbsentFromTheList()
    {
        var (owner, _, _) = await SignInAsync("owner");
        var (intruder, _, _) = await SignInAsync("intruder");
        await CreateNoteAsync(owner, "Private");
        await CreateNoteAsync(intruder, "Mine");

        (await TitlesAsync(owner)).ShouldBe(["Private"]);
        (await TitlesAsync(intruder)).ShouldBe(["Mine"]);
    }

    [Fact]
    public async Task AnotherUsersNote_IsNotReadable_UpdatableOrDeletable_AndStaysUntouched()
    {
        var (owner, _, _) = await SignInAsync("owner");
        var (intruder, _, _) = await SignInAsync("intruder");
        var note = await CreateNoteAsync(owner, "Private", "Secret");

        using var read = await intruder.GetAsync(new Uri($"{NotesRoute}/{note.Note.Id}", UriKind.Relative), TestToken);
        using var update = await UpdateNoteAsync(intruder, note.Note.Id, "Hijacked");
        using var delete = await DeleteNoteAsync(intruder, note.Note.Id);

        foreach (var response in new[] { read, update, delete })
        {
            response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
            (await ApiJson.ReadProblemAsync(response, TestToken)).Code.ShouldBe("note.notFound");
        }

        var still = await ReadNoteAsync(owner, note.Note.Id);
        still.Note.ShouldBe(note.Note);
        still.ETag.ShouldBe(note.ETag, "the refused writes did not move the version");
    }

    /// <summary>
    /// Compared as whole responses with the id factored out: a foreign note and a note that never existed
    /// must differ in nothing a caller can see, on every route and with a precondition attached.
    /// </summary>
    [Fact]
    public async Task AForeignNote_AndAMissingOne_AnswerIdentically()
    {
        var (owner, _, _) = await SignInAsync("owner");
        var (intruder, _, _) = await SignInAsync("intruder");
        var foreign = (await CreateNoteAsync(owner)).Note.Id;
        var missing = Guid.CreateVersion7();

        foreach (string? ifMatch in new string?[] { null, "*", "\"1\"" })
        {
            (await Answer(intruder, foreign, ifMatch)).ShouldBe(await Answer(intruder, missing, ifMatch));
        }

        static async Task<(HttpStatusCode, string?, string?, string?)[]> Answer(HttpClient client, Guid id, string? ifMatch)
        {
            using var read = await client.GetAsync(new Uri($"{NotesRoute}/{id}", UriKind.Relative), TestToken);
            using var update = await UpdateNoteAsync(client, id, "Title", "Body", ifMatch);
            using var delete = await DeleteNoteAsync(client, id, ifMatch);

            var results = new List<(HttpStatusCode, string?, string?, string?)>();

            foreach (var response in new[] { read, update, delete })
            {
                var problem = await ApiJson.ReadProblemAsync(response, TestToken);

                results.Add((
                    response.StatusCode,
                    problem.Code,
                    problem.Title,
                    problem.Detail?.Replace(id.ToString(), "{id}", StringComparison.Ordinal)));
            }

            return [.. results];
        }
    }

    private static async Task<IReadOnlyList<string>> TitlesAsync(HttpClient client)
    {
        using var response = await client.GetAsync(new Uri(NotesRoute, UriKind.Relative), TestToken);

        var page = await ApiJson.ReadAsync<PagedResponse<NoteSummaryResponse>>(response, TestToken);

        return [.. page.Items.Select(item => item.Title)];
    }
}

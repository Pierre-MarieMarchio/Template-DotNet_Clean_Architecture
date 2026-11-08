using System.Net;
using System.Net.Http.Json;
using AppTemplate.Api.Features.Notes.Contracts.Requests;
using AppTemplate.Api.Features.Notes.Contracts.Responses;
using AppTemplate.Api.IntegrationTests.Infrastructure;

namespace AppTemplate.Api.IntegrationTests.Notes;

public sealed record NoteWithTag(NoteResponse Note, string ETag);

public abstract class NotesTestBase(ApiFixture fixture) : IntegrationTestBase(fixture)
{
    protected const string NotesRoute = "/api/v1/notes";

    protected static async Task<NoteWithTag> CreateNoteAsync(
        HttpClient client,
        string title = "Groceries",
        string body = "Milk, eggs")
    {
        ArgumentNullException.ThrowIfNull(client);

        using var response = await client.PostAsJsonAsync(NotesRoute, new CreateNoteRequest(title, body), TestToken);

        return await ReadWrittenNoteAsync(response, HttpStatusCode.Created);
    }

    protected static async Task<NoteWithTag> ReadNoteAsync(HttpClient client, Guid noteId)
    {
        ArgumentNullException.ThrowIfNull(client);

        using var response = await client.GetAsync(new Uri($"{NotesRoute}/{noteId}", UriKind.Relative), TestToken);

        return await ReadWrittenNoteAsync(response, HttpStatusCode.OK);
    }

    protected static Task<HttpResponseMessage> UpdateNoteAsync(
        HttpClient client,
        Guid noteId,
        string title,
        string body = "Updated",
        string? ifMatch = null)
    {
        ArgumentNullException.ThrowIfNull(client);

        var request = new HttpRequestMessage(HttpMethod.Put, $"{NotesRoute}/{noteId}")
        {
            Content = JsonContent.Create(new UpdateNoteRequest(title, body)),
        };

        if (ifMatch is not null)
        {
            request.Headers.TryAddWithoutValidation("If-Match", ifMatch);
        }

        return client.SendAsync(request, TestToken);
    }

    protected static Task<HttpResponseMessage> DeleteNoteAsync(HttpClient client, Guid noteId, string? ifMatch = null)
    {
        ArgumentNullException.ThrowIfNull(client);

        var request = new HttpRequestMessage(HttpMethod.Delete, $"{NotesRoute}/{noteId}");

        if (ifMatch is not null)
        {
            request.Headers.TryAddWithoutValidation("If-Match", ifMatch);
        }

        return client.SendAsync(request, TestToken);
    }

    protected static async Task<NoteWithTag> ReadWrittenNoteAsync(HttpResponseMessage response, HttpStatusCode expected)
    {
        ArgumentNullException.ThrowIfNull(response);

        if (response.StatusCode != expected)
        {
            throw new InvalidOperationException(
                $"Expected {(int)expected} and got {(int)response.StatusCode}: "
                + await response.Content.ReadAsStringAsync(TestToken));
        }

        string tag = response.Headers.ETag?.ToString()
            ?? throw new InvalidOperationException("The response published no ETag.");

        return new NoteWithTag(await ApiJson.ReadAsync<NoteResponse>(response, TestToken), tag);
    }
}

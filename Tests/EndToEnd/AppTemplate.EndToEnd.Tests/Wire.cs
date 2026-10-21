using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Shouldly;

namespace AppTemplate.EndToEnd.Tests;

internal static class Wire
{
    internal static async Task<string> ExpectAsync(
        HttpResponseMessage response,
        HttpStatusCode status,
        CancellationToken cancellationToken)
    {
        string body = await response.Content.ReadAsStringAsync(cancellationToken);

        response.StatusCode.ShouldBe(
            status,
            $"{response.RequestMessage?.Method} {response.RequestMessage?.RequestUri} answered: {body}");

        return body;
    }

    internal static async Task<JsonElement> ExpectJsonAsync(
        HttpResponseMessage response,
        HttpStatusCode status,
        CancellationToken cancellationToken)
    {
        string body = await ExpectAsync(response, status, cancellationToken);

        using var document = JsonDocument.Parse(body);

        return document.RootElement.Clone();
    }

    internal static async Task<HttpResponseMessage> SendAsync(
        HttpClient client,
        HttpMethod method,
        string path,
        CancellationToken cancellationToken,
        object? body = null,
        string? bearer = null)
    {
        using var request = new HttpRequestMessage(method, new Uri(path, UriKind.Relative));

        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        if (bearer is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        }

        return await client.SendAsync(request, cancellationToken);
    }

    internal static string Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()!
            : throw new InvalidOperationException($"'{name}' is not a string member of {element}.");
}

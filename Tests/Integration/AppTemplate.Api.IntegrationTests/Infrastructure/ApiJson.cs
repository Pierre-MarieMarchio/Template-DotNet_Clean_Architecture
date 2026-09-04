using System.Text.Json;
using System.Text.RegularExpressions;

namespace AppTemplate.Api.IntegrationTests.Infrastructure;

/// <summary>The RFC 7807 fields the suite asserts on, read out of a response body.</summary>
/// <param name="Code">The <c>code</c> extension — the stable discriminator clients branch on.</param>
/// <param name="Body">The raw body, so a failing assertion says what actually came back.</param>
public sealed record ProblemResponse(int? Status, string? Title, string? Detail, string? Code, string Body)
{
    /// <summary>
    /// <see cref="Body"/> with the <c>traceId</c> removed, for comparing two responses that must be
    /// indistinguishable to a caller.
    /// </summary>
    /// <remarks>
    /// Every problem document carries a <c>traceId</c>, and it identifies the request rather than its
    /// outcome — so two responses to two requests always differ there, and comparing whole bodies
    /// would assert that two requests were the same request. What must match is everything else.
    /// </remarks>
    public string BodyWithoutTraceId =>
        Regex.Replace(Body, "\"traceId\"\\s*:\\s*\"[^\"]*\"", "\"traceId\":\"·\"", RegexOptions.None, _timeout);

    private static readonly TimeSpan _timeout = TimeSpan.FromSeconds(5);
}

/// <remarks>
/// Bodies are read as text and parsed, rather than through <c>ReadFromJsonAsync</c>, for two
/// reasons: the error responses are <c>application/problem+json</c>, and a failed assertion should
/// be able to quote what the server sent instead of reporting a deserialisation exception.
/// </remarks>
public static class ApiJson
{
    private static readonly JsonSerializerOptions _options = new(JsonSerializerDefaults.Web);

    public static async Task<T> ReadAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(response);

        string body = await response.Content.ReadAsStringAsync(cancellationToken);

        try
        {
            return JsonSerializer.Deserialize<T>(body, _options)
                ?? throw new InvalidOperationException($"The body deserialised to null: {body}");
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException(
                $"Could not read a {typeof(T).Name} from a {(int)response.StatusCode} response: {body}",
                exception);
        }
    }

    public static async Task<ProblemResponse> ReadProblemAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(response);

        string body = await response.Content.ReadAsStringAsync(cancellationToken);

        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;

        return new ProblemResponse(
            ReadInt32(root, "status"),
            ReadString(root, "title"),
            ReadString(root, "detail"),
            ReadString(root, "code"),
            body);
    }

    private static string? ReadString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static int? ReadInt32(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number
            ? value.GetInt32()
            : null;
}

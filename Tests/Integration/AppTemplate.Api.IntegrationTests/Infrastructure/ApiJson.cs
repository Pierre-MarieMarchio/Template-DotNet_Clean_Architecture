using System.Text.Json;
using System.Text.RegularExpressions;

namespace AppTemplate.Api.IntegrationTests.Infrastructure;

public sealed record ProblemResponse(int? Status, string? Title, string? Detail, string? Code, string Body)
{
    public string BodyWithoutTraceId =>
        Regex.Replace(Body, "\"traceId\"\\s*:\\s*\"[^\"]*\"", "\"traceId\":\"·\"", RegexOptions.None, _timeout);

    private static readonly TimeSpan _timeout = TimeSpan.FromSeconds(5);
}

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

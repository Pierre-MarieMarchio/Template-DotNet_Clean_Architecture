using System.Collections.Concurrent;
using System.Net;
using System.Text;

namespace AppTemplate.Client.UnitTests.TestSupport;

internal sealed record SentRequest(HttpMethod Method, string Path, string? Authorization, string? RefreshTokenMode, string Body);

internal sealed class StubApi(Func<SentRequest, Task<HttpResponseMessage>> answer) : HttpMessageHandler
{
    public ConcurrentQueue<SentRequest> Sent { get; } = new();

    public IEnumerable<SentRequest> SentTo(string path) =>
        Sent.Where(request => string.Equals(request.Path, path, StringComparison.Ordinal));

    public static HttpResponseMessage Json(HttpStatusCode status, string body, string contentType = "application/json") =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, contentType) };

    public static HttpResponseMessage Problem(HttpStatusCode status, string body) =>
        Json(status, body, "application/problem+json");

    public static string Tokens(string accessToken, string? refreshToken) =>
        refreshToken is null
            ? $$"""{"accessToken":"{{accessToken}}","accessTokenExpiresAt":"2030-01-01T00:00:00Z","refreshTokenExpiresAt":"2030-01-02T00:00:00Z"}"""
            : $$"""{"accessToken":"{{accessToken}}","accessTokenExpiresAt":"2030-01-01T00:00:00Z","refreshToken":"{{refreshToken}}","refreshTokenExpiresAt":"2030-01-02T00:00:00Z"}""";

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        string body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
        var sent = new SentRequest(
            request.Method,
            request.RequestUri!.AbsolutePath,
            request.Headers.Authorization?.ToString(),
            request.Headers.TryGetValues("X-Refresh-Token-Mode", out var modes) ? string.Join(",", modes) : null,
            body);

        Sent.Enqueue(sent);

        var response = await answer(sent);
        response.RequestMessage = request;

        return response;
    }
}

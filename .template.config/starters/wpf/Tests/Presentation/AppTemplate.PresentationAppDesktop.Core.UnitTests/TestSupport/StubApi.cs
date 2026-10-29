using System.Net;
using System.Text;

namespace AppTemplate.PresentationApp.Core.UnitTests.TestSupport;

internal sealed class StubApi(Func<HttpRequestMessage, HttpResponseMessage> answer) : HttpMessageHandler
{
    public List<string> Sent { get; } = [];

    public static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    public static HttpResponseMessage Problem(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/problem+json") };

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Sent.Add(request.RequestUri!.AbsolutePath);

        return Task.FromResult(answer(request));
    }
}

namespace AppTemplate.FrameworkSite.UnitTests.TestSupport;

internal sealed class StubApi(Func<HttpRequestMessage, HttpResponseMessage> answer) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        Task.FromResult(answer(request));
}

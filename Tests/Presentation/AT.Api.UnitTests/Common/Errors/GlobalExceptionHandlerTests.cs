using System.Text.Json;
using AppTemplate.Api.Common.Errors;
using AppTemplate.Api.UnitTests.TestSupport;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Http.Timeouts;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Xunit;

namespace AppTemplate.Api.UnitTests.Common.Errors;

public sealed class GlobalExceptionHandlerTests
{
    [Fact]
    public async Task TryHandleAsync_ClientCancellation_OverwritesTheFrameworksDefaultFiveHundred_WithFourNinetyNine()
    {
        var handler = new GlobalExceptionHandler(NullLogger<GlobalExceptionHandler>.Instance);
        var httpContext = HttpContextFactory.Create();
        httpContext.Response.StatusCode = StatusCodes.Status500InternalServerError;

        bool handled = await handler.TryHandleAsync(httpContext, new OperationCanceledException(), TestContext.Current.CancellationToken);

        handled.ShouldBeTrue();
        httpContext.Response.StatusCode.ShouldBe(StatusCodes.Status499ClientClosedRequest);
    }

    [Fact]
    public async Task TryHandleAsync_ClientCancellation_WritesNoBody()
    {
        var handler = new GlobalExceptionHandler(NullLogger<GlobalExceptionHandler>.Instance);
        var httpContext = HttpContextFactory.Create();
        using var body = new MemoryStream();
        httpContext.Response.Body = body;

        await handler.TryHandleAsync(httpContext, new OperationCanceledException(), TestContext.Current.CancellationToken);

        body.Length.ShouldBe(0);
    }

    [Fact]
    public async Task TryHandleAsync_ClientCancellation_WhenResponseAlreadyStarted_DoesNotThrow()
    {
        var handler = new GlobalExceptionHandler(NullLogger<GlobalExceptionHandler>.Instance);
        var httpContext = HttpContextFactory.Create();
        httpContext.Features.Set<IHttpResponseFeature>(new StartedResponseFeature());

        bool handled = await handler.TryHandleAsync(httpContext, new OperationCanceledException(), TestContext.Current.CancellationToken);

        handled.ShouldBeTrue();
    }

    [Fact]
    public async Task TryHandleAsync_OperationCanceledException_WithTimeoutFeature_Answers504NotFourNinetyNine()
    {
        var handler = new GlobalExceptionHandler(NullLogger<GlobalExceptionHandler>.Instance);
        var httpContext = HttpContextFactory.Create();
        httpContext.Features.Set<IHttpRequestTimeoutFeature>(new FakeRequestTimeoutFeature());
        using var body = new MemoryStream();
        httpContext.Response.Body = body;

        bool handled = await handler.TryHandleAsync(httpContext, new OperationCanceledException(), TestContext.Current.CancellationToken);

        handled.ShouldBeTrue();
        httpContext.Response.StatusCode.ShouldBe(StatusCodes.Status504GatewayTimeout);

        body.Position = 0;
        using var document = await JsonDocument.ParseAsync(body, cancellationToken: TestContext.Current.CancellationToken);
        document.RootElement.GetProperty("code").GetString().ShouldBe("request.timeout");
    }

    [Fact]
    public async Task TryHandleAsync_OperationCanceledException_WithoutTimeoutFeature_StaysFourNinetyNine()
    {
        var handler = new GlobalExceptionHandler(NullLogger<GlobalExceptionHandler>.Instance);
        var httpContext = HttpContextFactory.Create();

        await handler.TryHandleAsync(httpContext, new OperationCanceledException(), TestContext.Current.CancellationToken);

        httpContext.Response.StatusCode.ShouldBe(StatusCodes.Status499ClientClosedRequest);
    }

    private sealed class FakeRequestTimeoutFeature : IHttpRequestTimeoutFeature
    {
        public CancellationToken RequestTimeoutToken => CancellationToken.None;

        public void DisableTimeout()
        {
        }
    }

    private sealed class StartedResponseFeature : IHttpResponseFeature
    {
        public int StatusCode { get; set; }

        public string? ReasonPhrase { get; set; }

        public IHeaderDictionary Headers { get; set; } = new HeaderDictionary();

        public Stream Body { get; set; } = Stream.Null;

        public bool HasStarted => true;

        public void OnStarting(Func<object, Task> callback, object state)
        {
        }

        public void OnCompleted(Func<object, Task> callback, object state)
        {
        }
    }
}

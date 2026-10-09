using System.Text;
using System.Text.Json;
using AppTemplate.Api.Common.Hosting;
using AppTemplate.Api.UnitTests.TestSupport;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Routing;
using Shouldly;
using Xunit;

namespace AppTemplate.Api.UnitTests.Common.Hosting;

/// <summary>
/// The request-size middleware and the raised limit one endpoint may declare, driven directly:
/// this project starts no server, and TestServer would not apply Kestrel's limit anyway.
/// </summary>
public sealed class RequestBodyLimitTests
{
    private const long _globalLimit = 64 * 1024;
    private const long _raisedLimit = 1024 * 1024;
    private const string _boundary = "test-boundary";

    #region The middleware

    [Fact]
    public async Task ABodyOverTheGlobalLimit_Answers413_WithoutReachingTheEndpoint()
    {
        var httpContext = AContext(contentLength: _globalLimit + 1);
        bool reached = false;

        await RequestLimitsExtensions.LimitAsync(httpContext, _ => { reached = true; return Task.CompletedTask; }, _globalLimit);

        reached.ShouldBeFalse();
        await ShouldBeTooLargeAsync(httpContext);
    }

    [Fact]
    public async Task AnEndpointWithARaisedLimit_AcceptsABodyAboveTheGlobalOne()
    {
        var httpContext = AContext(contentLength: _globalLimit * 4, raised: true);
        bool reached = false;

        await RequestLimitsExtensions.LimitAsync(httpContext, _ => { reached = true; return Task.CompletedTask; }, _globalLimit);

        reached.ShouldBeTrue();
    }

    [Fact]
    public async Task AnEndpointWithARaisedLimit_StillAnswers413_AboveItsOwn()
    {
        var httpContext = AContext(contentLength: _raisedLimit + 1, raised: true);
        bool reached = false;

        await RequestLimitsExtensions.LimitAsync(httpContext, _ => { reached = true; return Task.CompletedTask; }, _globalLimit);

        reached.ShouldBeFalse();
        await ShouldBeTooLargeAsync(httpContext);
    }

    /// <summary>A chunked body declares no length: the server's limit is what stops it, so it is raised too.</summary>
    [Fact]
    public async Task AnEndpointWithARaisedLimit_RaisesTheServersLimitForThisRequest()
    {
        var serverLimit = new ServerLimit { MaxRequestBodySize = _globalLimit };
        var httpContext = AContext(contentLength: null, raised: true);
        httpContext.Features.Set<IHttpMaxRequestBodySizeFeature>(serverLimit);

        await RequestLimitsExtensions.LimitAsync(httpContext, _ => Task.CompletedTask, _globalLimit);

        serverLimit.MaxRequestBodySize.ShouldBe(_raisedLimit);
    }

    [Fact]
    public async Task AnEndpointWithARaisedLimit_CapsAMultipartBodyAtTheSameValue()
    {
        var httpContext = AContext(contentLength: null, raised: true);
        httpContext.Request.ContentType = $"multipart/form-data; boundary={_boundary}";
        httpContext.Request.Body = new MemoryStream(AMultipartBody(fileBytes: (int)_raisedLimit + 1));

        await RequestLimitsExtensions.LimitAsync(httpContext, _ => Task.CompletedTask, _globalLimit);

        await Should.ThrowAsync<InvalidDataException>(() => httpContext.Request.ReadFormAsync(TestContext.Current.CancellationToken));
    }

    #endregion

    #region The filter

    /// <summary>Kestrel's own refusal while the form is read becomes the same 413, not a 400 from binding.</summary>
    [Fact]
    public async Task ABodyThatOverrunsWhileTheFormIsRead_Answers413()
    {
        var httpContext = AContext(contentLength: null);
        httpContext.Request.ContentType = $"multipart/form-data; boundary={_boundary}";
        httpContext.Request.Body = new OverrunningStream();
        var context = AResourceContext(httpContext);
        bool reached = false;

        await new RaisedLimit().OnResourceExecutionAsync(context, () => { reached = true; return Task.FromResult<ResourceExecutedContext>(null!); });

        reached.ShouldBeFalse();
        context.Result.ShouldBeOfType<EmptyResult>();
        await ShouldBeTooLargeAsync(httpContext);
    }

    /// <summary>A malformed form is left to model binding, which answers 400 from the same cached failure.</summary>
    [Fact]
    public async Task AMalformedForm_IsLeftToModelBinding()
    {
        var httpContext = AContext(contentLength: null);
        httpContext.Request.ContentType = $"multipart/form-data; boundary={_boundary}";
        httpContext.Request.Body = new MemoryStream("not a multipart body"u8.ToArray());
        var context = AResourceContext(httpContext);
        bool reached = false;

        await new RaisedLimit().OnResourceExecutionAsync(context, () => { reached = true; return Task.FromResult<ResourceExecutedContext>(null!); });

        reached.ShouldBeTrue();
        context.Result.ShouldBeNull();
    }

    [Fact]
    public async Task AWellFormedForm_IsReadOnceAndPassedOn()
    {
        var httpContext = AContext(contentLength: null);
        httpContext.Request.ContentType = $"multipart/form-data; boundary={_boundary}";
        httpContext.Request.Body = new MemoryStream(AMultipartBody(fileBytes: 10));
        var context = AResourceContext(httpContext);
        bool reached = false;

        await new RaisedLimit().OnResourceExecutionAsync(context, () => { reached = true; return Task.FromResult<ResourceExecutedContext>(null!); });

        reached.ShouldBeTrue();
        httpContext.Request.Form.Files.Count.ShouldBe(1);
    }

    #endregion

    private static DefaultHttpContext AContext(long? contentLength, bool raised = false)
    {
        var httpContext = HttpContextFactory.Create();
        httpContext.Request.ContentLength = contentLength;
        httpContext.Response.Body = new MemoryStream();

        if (raised)
        {
            httpContext.SetEndpoint(new Endpoint(
                _ => Task.CompletedTask,
                new EndpointMetadataCollection(new RaisedLimit()),
                "upload"));
        }

        return httpContext;
    }

    private static ResourceExecutingContext AResourceContext(HttpContext httpContext) =>
        new(
            new ActionContext(httpContext, new RouteData(), new ActionDescriptor()),
            new List<IFilterMetadata>(),
            new List<IValueProviderFactory>());

    private static byte[] AMultipartBody(int fileBytes)
    {
        var body = new StringBuilder()
            .Append($"--{_boundary}\r\n")
            .Append("Content-Disposition: form-data; name=\"file\"; filename=\"a.txt\"\r\n")
            .Append("Content-Type: text/plain\r\n\r\n")
            .Append(new string('a', fileBytes))
            .Append($"\r\n--{_boundary}--\r\n");

        return Encoding.UTF8.GetBytes(body.ToString());
    }

    private static async Task ShouldBeTooLargeAsync(HttpContext httpContext)
    {
        httpContext.Response.StatusCode.ShouldBe(StatusCodes.Status413PayloadTooLarge);
        httpContext.Response.ContentType.ShouldBe("application/problem+json");

        httpContext.Response.Body.Position = 0;
        using var problem = await JsonDocument.ParseAsync(httpContext.Response.Body, cancellationToken: TestContext.Current.CancellationToken);
        problem.RootElement.GetProperty("code").GetString().ShouldBe("request.tooLarge");
    }

    private sealed class RaisedLimit : RequestBodyLimitAttribute
    {
        public override long MaxRequestBodyBytes(IServiceProvider services) => _raisedLimit;
    }

    private sealed class ServerLimit : IHttpMaxRequestBodySizeFeature
    {
        public bool IsReadOnly => false;

        public long? MaxRequestBodySize { get; set; }
    }

    /// <summary>What Kestrel's body reader throws once a request passes its limit.</summary>
    private sealed class OverrunningStream : MemoryStream
    {
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            throw new BadHttpRequestException("Request body too large.", StatusCodes.Status413PayloadTooLarge);

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            throw new BadHttpRequestException("Request body too large.", StatusCodes.Status413PayloadTooLarge);
    }
}

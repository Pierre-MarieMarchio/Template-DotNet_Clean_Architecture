using System.Net;
using AppTemplate.Api.Features.Auth.Composition;
using Microsoft.AspNetCore.Http;
using Shouldly;
using Xunit;

namespace AppTemplate.Api.UnitTests.Features.Auth.Composition;

public sealed class HttpContextSecurityEventOriginProviderTests
{
    [Fact]
    public void TheOrigin_ComesFromTheCurrentRequest()
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Connection.RemoteIpAddress = IPAddress.Parse("203.0.113.7");
        httpContext.Request.Headers.UserAgent = "Mozilla/5.0";

        var provider = new HttpContextSecurityEventOriginProvider(new FixedAccessor(httpContext));

        provider.IpAddress.ShouldBe("203.0.113.7");
        provider.UserAgent.ShouldBe("Mozilla/5.0");
    }

    [Fact]
    public void ARequestWithNoUserAgent_AnswersNullForIt()
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Connection.RemoteIpAddress = IPAddress.Parse("203.0.113.7");

        var provider = new HttpContextSecurityEventOriginProvider(new FixedAccessor(httpContext));

        provider.IpAddress.ShouldBe("203.0.113.7");
        provider.UserAgent.ShouldBeNull();
    }

    [Fact]
    public void WithoutARequest_TheOriginIsUnknown()
    {
        var provider = new HttpContextSecurityEventOriginProvider(new FixedAccessor(null));

        provider.IpAddress.ShouldBeNull();
        provider.UserAgent.ShouldBeNull();
    }

    private sealed class FixedAccessor(HttpContext? httpContext) : IHttpContextAccessor
    {
        public HttpContext? HttpContext { get; set; } = httpContext;
    }
}

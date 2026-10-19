using System.Net;
using AppTemplate.Api.Features.Auth.Composition;
using Microsoft.AspNetCore.Http;
using Shouldly;
using Xunit;

namespace AppTemplate.Api.UnitTests.Features.Auth.Composition;

/// <summary>
/// The origin a security event is stamped with is whatever the request carried, and nothing when
/// there was no request — the maintenance timer's case — because an absent origin is the truthful
/// answer rather than a failure worth throwing on.
/// </summary>
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

    /// <summary>The background scope's case: no request behind the origin at all.</summary>
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

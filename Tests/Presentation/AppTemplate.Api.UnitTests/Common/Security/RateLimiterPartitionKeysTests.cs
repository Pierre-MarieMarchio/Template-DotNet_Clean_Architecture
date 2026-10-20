using System.Net;
using System.Security.Claims;
using AppTemplate.Api.Common.Security;
using Microsoft.AspNetCore.Http;
using Shouldly;
using Xunit;

namespace AppTemplate.Api.UnitTests.Common.Security;

public sealed class RateLimiterPartitionKeysTests
{
    [Fact]
    public void ForAddress_ReturnsThePrefixedAddress_RegardlessOfAuthentication()
    {
        var httpContext = CreateHttpContext("203.0.113.7", authenticated: true, subject: Guid.NewGuid());

        RateLimiterPartitionKeys.ForAddress(httpContext).ShouldBe("ip:203.0.113.7");
    }

    [Fact]
    public void ForAddress_FallsBackToUnknown_WhenTheAddressCannotBeRead()
    {
        var httpContext = new DefaultHttpContext();

        RateLimiterPartitionKeys.ForAddress(httpContext).ShouldBe("ip:unknown");
    }

    [Fact]
    public void ForAddress_KeepsTwoDistinctAddressesApart()
    {
        var first = CreateHttpContext("203.0.113.7", authenticated: false, subject: null);
        var second = CreateHttpContext("203.0.113.8", authenticated: false, subject: null);

        RateLimiterPartitionKeys.ForAddress(first).ShouldNotBe(RateLimiterPartitionKeys.ForAddress(second));
    }

    private static DefaultHttpContext CreateHttpContext(string address, bool authenticated, Guid? subject)
    {
        var httpContext = new DefaultHttpContext
        {
            Connection = { RemoteIpAddress = IPAddress.Parse(address) },
        };

        if (authenticated)
        {
            var claims = subject is { } userId ? [new Claim(ClaimTypes.NameIdentifier, userId.ToString())] : new List<Claim>();
            var identity = new ClaimsIdentity(claims, authenticationType: "Test");
            httpContext.User = new ClaimsPrincipal(identity);
        }

        return httpContext;
    }
}

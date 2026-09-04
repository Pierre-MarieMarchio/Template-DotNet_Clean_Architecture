using System.Net;
using System.Security.Claims;
using AppTemplate.Api.Common.Security;
using Microsoft.AspNetCore.Http;
using Shouldly;
using Xunit;

namespace AppTemplate.Api.UnitTests.Common.Security;

/// <summary>
/// The partition key selector in isolation, independent of where in the pipeline it ends up wired.
/// </summary>
public sealed class RateLimiterPartitionKeysTests
{
    /// <summary>
    /// An authenticated caller gets the same key as an anonymous one from the same address. That is
    /// the decision, not an omission: partitioning by identity would require authentication to run
    /// before the limiter, which would make every rejected request pay for a bearer validation and
    /// its security stamp read first.
    /// </summary>
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

    /// <summary>
    /// Two callers from different addresses must never fold into one partition, which is the whole
    /// property the limiter rests on.
    /// </summary>
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

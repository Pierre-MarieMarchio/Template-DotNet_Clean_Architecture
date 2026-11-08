using System.Globalization;
using System.Security.Claims;
using AppTemplate.Api.Common.Security;
using Microsoft.AspNetCore.Http;
using Shouldly;
using Xunit;

namespace AppTemplate.Api.UnitTests.Common.Security;

public sealed class CurrentAccessTokenTests
{
    private static readonly DateTimeOffset _issuedAt = new(2026, 8, 3, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ATokenCarryingIat_AnswersItsIssueInstant()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim("iat", _issuedAt.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture))]));

        IssuedAtOf(principal).ShouldBe(_issuedAt);
    }

    [Fact]
    public void ATokenCarryingNoIatClaim_AnswersNull()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", "somebody")]));

        IssuedAtOf(principal).ShouldBeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData("not a number")]
    public void AMalformedIatClaim_AnswersNull(string iat)
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim("iat", iat)]));

        IssuedAtOf(principal).ShouldBeNull();
    }

    [Fact]
    public void ARequestWithNoHttpContext_AnswersNull()
    {
        var accessor = new HttpContextAccessor();

        new CurrentAccessToken(accessor).IssuedAt.ShouldBeNull();
    }

    private static DateTimeOffset? IssuedAtOf(ClaimsPrincipal principal)
    {
        var accessor = new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext { User = principal },
        };

        return new CurrentAccessToken(accessor).IssuedAt;
    }
}

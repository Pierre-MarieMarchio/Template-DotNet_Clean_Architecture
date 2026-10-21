using AppTemplate.Api.Common.Security;
using Shouldly;
using Xunit;

namespace AppTemplate.Api.UnitTests.Common.Security;

public sealed class RateLimitingOptionsValidatorTests
{
    private readonly RateLimitingOptionsValidator _validator = new();

    [Fact]
    public void Validate_Succeeds_ForTheDefaults()
    {
        _validator.Validate(name: null, new RateLimitingOptions()).Succeeded.ShouldBeTrue();
    }

    [Theory]
    [InlineData("00:00:01")]
    [InlineData("1.00:00:00")]
    public void Validate_Succeeds_AtEachWindowBoundary(string window)
    {
        var options = new RateLimitingOptions { Window = TimeSpan.Parse(window) };

        _validator.Validate(name: null, options).Succeeded.ShouldBeTrue();
    }

    [Theory]
    [InlineData("00:00:00")]
    [InlineData("00:00:00.500")]
    [InlineData("1.00:00:01")]
    public void Validate_Fails_WhenTheWindowIsOutOfRange(string window)
    {
        var options = new RateLimitingOptions { Window = TimeSpan.Parse(window) };

        var result = _validator.Validate(name: null, options);

        result.Failed.ShouldBeTrue();
        result.FailureMessage.ShouldContain("RateLimiting:Window");
    }

    [Theory]
    [InlineData(0)]
    public void Validate_Fails_WhenAPermitLimitIsBelowOne(int permits)
    {
        /*#if (localAuth)*/
        var options = new RateLimitingOptions { AuthenticationPermitLimit = permits, GlobalPermitLimit = permits };
        /*#else
        var options = new RateLimitingOptions { GlobalPermitLimit = permits };
        #endif*/

        var result = _validator.Validate(name: null, options);

        result.Failed.ShouldBeTrue();
        //#if (localAuth)
        result.FailureMessage.ShouldContain("RateLimiting:AuthenticationPermitLimit");
        //#endif
        result.FailureMessage.ShouldContain("RateLimiting:GlobalPermitLimit");
    }
}

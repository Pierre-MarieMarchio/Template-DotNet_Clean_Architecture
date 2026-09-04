using AppTemplate.Api.Common.Hosting;
using Shouldly;
using Xunit;

namespace AppTemplate.Api.UnitTests.Common.Hosting;

public sealed class ShutdownOptionsValidatorTests
{
    private readonly ShutdownOptionsValidator _validator = new();

    [Fact]
    public void Validate_Succeeds_ForTheDefaults()
    {
        var result = _validator.Validate(name: null, new ShutdownOptions());

        result.Succeeded.ShouldBeTrue();
    }

    [Theory]
    [InlineData("00:00:01")]
    [InlineData("00:10:00")]
    public void Validate_Succeeds_AtEachBoundary(string timeout)
    {
        var options = new ShutdownOptions { Timeout = TimeSpan.Parse(timeout) };

        _validator.Validate(name: null, options).Succeeded.ShouldBeTrue();
    }

    [Fact]
    public void Validate_Fails_WhenTimeoutIsZero()
    {
        var result = _validator.Validate(name: null, new ShutdownOptions { Timeout = TimeSpan.Zero });

        result.Failed.ShouldBeTrue();
        result.FailureMessage.ShouldContain("Shutdown:Timeout");
    }

    [Fact]
    public void Validate_Fails_WhenTimeoutIsNegative()
    {
        var options = new ShutdownOptions { Timeout = TimeSpan.FromSeconds(-1) };

        _validator.Validate(name: null, options).Failed.ShouldBeTrue();
    }

    [Fact]
    public void Validate_Fails_WhenTimeoutExceedsTenMinutes()
    {
        var options = new ShutdownOptions { Timeout = TimeSpan.FromMinutes(10) + TimeSpan.FromSeconds(1) };

        _validator.Validate(name: null, options).Failed.ShouldBeTrue();
    }

    [Fact]
    public void Validate_RejectsNull() =>
        Should.Throw<ArgumentNullException>(() => _validator.Validate(name: null, options: null!));
}

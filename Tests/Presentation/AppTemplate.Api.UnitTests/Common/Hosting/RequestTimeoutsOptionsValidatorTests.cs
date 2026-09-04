using AppTemplate.Api.Common.Hosting;
using Shouldly;
using Xunit;

namespace AppTemplate.Api.UnitTests.Common.Lifecycle;

public sealed class RequestTimeoutsOptionsValidatorTests
{
    private readonly RequestTimeoutsOptionsValidator _validator = new();

    [Fact]
    public void Validate_Succeeds_ForTheDefaults()
    {
        var result = _validator.Validate(name: null, new RequestTimeoutsOptions());

        result.Succeeded.ShouldBeTrue();
    }

    [Fact]
    public void Validate_Fails_WhenDefaultIsBelowOneSecond()
    {
        var options = new RequestTimeoutsOptions { Default = TimeSpan.FromMilliseconds(500) };

        var result = _validator.Validate(name: null, options);

        result.Failed.ShouldBeTrue();
        result.FailureMessage.ShouldContain("RequestTimeouts:Default");
    }

    [Fact]
    public void Validate_Fails_WhenDefaultExceedsOneHour()
    {
        var options = new RequestTimeoutsOptions
        {
            Default = TimeSpan.FromHours(1) + TimeSpan.FromSeconds(1),
            Extended = TimeSpan.FromHours(1) + TimeSpan.FromSeconds(2),
        };

        _validator.Validate(name: null, options).Failed.ShouldBeTrue();
    }

    [Fact]
    public void Validate_Fails_WhenExtendedIsBelowOneSecond()
    {
        // Also below Default, which independently fails — this asserts the range check still fires
        // rather than being masked by the ordering check below.
        var options = new RequestTimeoutsOptions
        {
            Default = TimeSpan.FromSeconds(1),
            Extended = TimeSpan.FromMilliseconds(1),
        };

        var result = _validator.Validate(name: null, options);

        result.Failed.ShouldBeTrue();
        result.FailureMessage.ShouldContain("RequestTimeouts:Extended");
    }

    [Fact]
    public void Validate_Fails_WhenExtendedDoesNotExceedDefault()
    {
        var options = new RequestTimeoutsOptions
        {
            Default = TimeSpan.FromMinutes(5),
            Extended = TimeSpan.FromMinutes(5),
        };

        var result = _validator.Validate(name: null, options);

        result.Failed.ShouldBeTrue();
        result.FailureMessage.ShouldContain("RequestTimeouts:Extended");
        result.FailureMessage.ShouldContain("RequestTimeouts:Default");
    }

    [Fact]
    public void Validate_Fails_WhenExtendedIsBelowDefault()
    {
        var options = new RequestTimeoutsOptions
        {
            Default = TimeSpan.FromMinutes(5),
            Extended = TimeSpan.FromMinutes(4),
        };

        _validator.Validate(name: null, options).Failed.ShouldBeTrue();
    }

    [Fact]
    public void Validate_RejectsNull() =>
        Should.Throw<ArgumentNullException>(() => _validator.Validate(name: null, options: null!));
}

using AppTemplate.Api.Common.Hosting;
using Shouldly;
using Xunit;

namespace AppTemplate.Api.UnitTests.Common.Hosting;

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
        };

        _validator.Validate(name: null, options).Failed.ShouldBeTrue();
    }

    [Fact]
    public void Validate_RejectsNull() =>
        Should.Throw<ArgumentNullException>(() => _validator.Validate(name: null, options: null!));
}

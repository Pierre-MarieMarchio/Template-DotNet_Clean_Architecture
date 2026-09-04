using AppTemplate.Api.Common.Observability;
using Shouldly;
using Xunit;

namespace AppTemplate.Api.UnitTests.Common.Observability;

public sealed class TelemetryOptionsValidatorTests
{
    private readonly TelemetryOptionsValidator _validator = new();

    [Fact]
    public void Validate_Succeeds_WhenDisabled_RegardlessOfOtherFields()
    {
        var options = new TelemetryOptions { Enabled = false, OtlpEndpoint = null, TracesSamplingRatio = -1 };

        _validator.Validate(null, options).Succeeded.ShouldBeTrue();
    }

    [Fact]
    public void Validate_Succeeds_WhenEnabled_WithTheDefaultSamplingRatio()
    {
        var options = new TelemetryOptions { Enabled = true, OtlpEndpoint = "http://localhost:4317" };

        _validator.Validate(null, options).Succeeded.ShouldBeTrue();
    }

    [Theory]
    [InlineData(0.001)]
    [InlineData(0.5)]
    [InlineData(1.0)]
    public void Validate_Succeeds_ForRatiosInTheOpenClosedUnitInterval(double ratio)
    {
        var options = new TelemetryOptions
        {
            Enabled = true,
            OtlpEndpoint = "http://localhost:4317",
            TracesSamplingRatio = ratio,
        };

        _validator.Validate(null, options).Succeeded.ShouldBeTrue();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(1.5)]
    [InlineData(double.NaN)]
    public void Validate_Fails_ForRatiosOutsideTheOpenClosedUnitInterval(double ratio)
    {
        var options = new TelemetryOptions
        {
            Enabled = true,
            OtlpEndpoint = "http://localhost:4317",
            TracesSamplingRatio = ratio,
        };

        var result = _validator.Validate(null, options);

        result.Succeeded.ShouldBeFalse();
        result.Failures.ShouldNotBeNull();
        result.Failures.ShouldContain(failure => failure.Contains(nameof(TelemetryOptions.TracesSamplingRatio)));
    }
}

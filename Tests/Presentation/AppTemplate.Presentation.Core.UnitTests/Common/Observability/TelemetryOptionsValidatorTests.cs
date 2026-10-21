using AppTemplate.Presentation.Core.Common.Observability;
using Shouldly;
using Xunit;

namespace AppTemplate.Presentation.Core.UnitTests.Common.Observability;

public sealed class TelemetryOptionsValidatorTests
{
    private const string _collector = "http://localhost:4317";

    private readonly TelemetryOptionsValidator _validator = new();

    [Fact]
    public void Validate_Succeeds_WhenDisabled_RegardlessOfEveryOtherField()
    {
        var options = new TelemetryOptions { Enabled = false, OtlpEndpoint = null, TracesSamplingRatio = -1 };

        _validator.Validate(name: null, options).Succeeded.ShouldBeTrue();
    }

    [Fact]
    public void Validate_Succeeds_WhenEnabled_WithAValidEndpoint()
    {
        var options = new TelemetryOptions { Enabled = true, OtlpEndpoint = _collector };

        _validator.Validate(name: null, options).Succeeded.ShouldBeTrue();
    }

    [Fact]
    public void Validate_Succeeds_WhenEnabled_WithTheDefaultSamplingRatio()
    {
        var options = new TelemetryOptions { Enabled = true, OtlpEndpoint = _collector };

        _validator.Validate(name: null, options).Succeeded.ShouldBeTrue();
    }

    [Fact]
    public void Validate_Fails_WhenEnabled_WithoutAnEndpoint()
    {
        var options = new TelemetryOptions { Enabled = true };

        var result = _validator.Validate(name: null, options);

        result.Failed.ShouldBeTrue();
        result.FailureMessage.ShouldContain(nameof(TelemetryOptions.OtlpEndpoint));
    }

    [Theory]
    [InlineData("not-a-url")]
    [InlineData("ftp://localhost:4317")]
    public void Validate_Fails_WhenEnabled_WithAnUnusableEndpoint(string endpoint)
    {
        var options = new TelemetryOptions { Enabled = true, OtlpEndpoint = endpoint };

        _validator.Validate(name: null, options).Failed.ShouldBeTrue();
    }

    [Fact]
    public void Validate_Fails_WhenServiceNameIsPresentButBlank()
    {
        var options = new TelemetryOptions
        {
            Enabled = true,
            OtlpEndpoint = _collector,
            ServiceName = "   ",
        };

        _validator.Validate(name: null, options).Failed.ShouldBeTrue();
    }

    [Theory]
    [InlineData(0.001)]
    [InlineData(1.0)]
    public void Validate_Succeeds_ForRatiosInTheOpenClosedUnitInterval(double ratio)
    {
        var options = new TelemetryOptions
        {
            Enabled = true,
            OtlpEndpoint = _collector,
            TracesSamplingRatio = ratio,
        };

        _validator.Validate(name: null, options).Succeeded.ShouldBeTrue();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1.5)]
    [InlineData(double.NaN)]
    public void Validate_Fails_ForRatiosOutsideTheOpenClosedUnitInterval(double ratio)
    {
        var options = new TelemetryOptions
        {
            Enabled = true,
            OtlpEndpoint = _collector,
            TracesSamplingRatio = ratio,
        };

        var result = _validator.Validate(name: null, options);

        result.Succeeded.ShouldBeFalse();
        result.Failures.ShouldNotBeNull();
        result.Failures.ShouldContain(
            failure => failure.Contains(nameof(TelemetryOptions.TracesSamplingRatio), StringComparison.Ordinal));
    }
}

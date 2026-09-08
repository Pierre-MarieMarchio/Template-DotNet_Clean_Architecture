using AppTemplate.Presentation.Core.Common.Observability;
using Shouldly;
using Xunit;

namespace AppTemplate.Presentation.Core.UnitTests.Common.Observability;

/// <summary>
/// One validator for one section, so a sampling ratio or an endpoint a deployment sets means the
/// same thing in every process it starts.
/// </summary>
public sealed class TelemetryOptionsValidatorTests
{
    private const string _collector = "http://localhost:4317";

    private readonly TelemetryOptionsValidator _validator = new();

    [Fact]
    public void Validate_Succeeds_WhenDisabled_EvenWithNoEndpoint()
    {
        var options = new TelemetryOptions { Enabled = false };

        _validator.Validate(name: null, options).Succeeded.ShouldBeTrue();
    }

    /// <summary>
    /// A ratio out of range on a host with telemetry switched off still boots — the whole validator
    /// short-circuits on <see cref="TelemetryOptions.Enabled"/>.
    /// </summary>
    [Fact]
    public void Validate_Succeeds_WhenDisabled_EvenWithAnImpossibleSamplingRatio()
    {
        var options = new TelemetryOptions { Enabled = false, TracesSamplingRatio = -1 };

        _validator.Validate(name: null, options).Succeeded.ShouldBeTrue();
    }

    /// <summary>Every other key at once, all unusable, and the section is still accepted.</summary>
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

    /// <summary>A ratio nobody set is not a reason to refuse a start-up.</summary>
    [Fact]
    public void Validate_Succeeds_WhenEnabled_WithTheDefaultSamplingRatio()
    {
        var options = new TelemetryOptions { Enabled = true, OtlpEndpoint = _collector };

        _validator.Validate(name: null, options).Succeeded.ShouldBeTrue();
    }

    /// <summary>
    /// The exporter's own fallback is an unannounced <c>localhost:4317</c>, so a deployment that
    /// enables telemetry and names no collector would export into nothing and be told nothing.
    /// </summary>
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

    /// <summary>
    /// Blank is not the same as absent: absent falls back on the host assembly's name, blank would
    /// announce a process under no name at all.
    /// </summary>
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
    [InlineData(0.5)]
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

    /// <summary>
    /// <see cref="double.NaN"/> is the case a range check written as its own negation would let
    /// through: every direct comparison against it is false.
    /// </summary>
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
            OtlpEndpoint = _collector,
            TracesSamplingRatio = ratio,
        };

        var result = _validator.Validate(name: null, options);

        result.Succeeded.ShouldBeFalse();
        result.Failures.ShouldNotBeNull();
        result.Failures.ShouldContain(
            failure => failure.Contains(nameof(TelemetryOptions.TracesSamplingRatio), StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_RejectsNull() =>
        Should.Throw<ArgumentNullException>(() => _validator.Validate(name: null, options: null!));
}

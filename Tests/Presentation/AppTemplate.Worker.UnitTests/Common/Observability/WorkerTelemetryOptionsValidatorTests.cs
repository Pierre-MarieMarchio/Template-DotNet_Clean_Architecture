using AppTemplate.Worker.Common.Observability;
using Shouldly;
using Xunit;

namespace AppTemplate.Worker.UnitTests.Common.Observability;

public sealed class WorkerTelemetryOptionsValidatorTests
{
    private readonly WorkerTelemetryOptionsValidator _validator = new();

    [Fact]
    public void Validate_Succeeds_WhenDisabled_EvenWithNoEndpoint()
    {
        var options = new WorkerTelemetryOptions { Enabled = false };

        _validator.Validate(name: null, options).Succeeded.ShouldBeTrue();
    }

    [Fact]
    public void Validate_Succeeds_WhenEnabled_WithAValidEndpoint()
    {
        var options = new WorkerTelemetryOptions { Enabled = true, OtlpEndpoint = "http://localhost:4317" };

        _validator.Validate(name: null, options).Succeeded.ShouldBeTrue();
    }

    [Fact]
    public void Validate_Fails_WhenEnabled_WithoutAnEndpoint()
    {
        var options = new WorkerTelemetryOptions { Enabled = true };

        var result = _validator.Validate(name: null, options);

        result.Failed.ShouldBeTrue();
        result.FailureMessage.ShouldContain("OtlpEndpoint");
    }

    [Theory]
    [InlineData("not-a-url")]
    [InlineData("ftp://localhost:4317")]
    public void Validate_Fails_WhenEnabled_WithAnUnusableEndpoint(string endpoint)
    {
        var options = new WorkerTelemetryOptions { Enabled = true, OtlpEndpoint = endpoint };

        _validator.Validate(name: null, options).Failed.ShouldBeTrue();
    }

    [Fact]
    public void Validate_Fails_WhenServiceNameIsPresentButBlank()
    {
        var options = new WorkerTelemetryOptions
        {
            Enabled = true,
            OtlpEndpoint = "http://localhost:4317",
            ServiceName = "   ",
        };

        _validator.Validate(name: null, options).Failed.ShouldBeTrue();
    }

    /// <summary>
    /// The same open-closed unit interval the Api host's validator enforces, on the same key of the
    /// same configuration section. These two validators are deliberate twins, so a ratio one of them
    /// accepts and the other refuses is the bug this covers.
    /// </summary>
    [Theory]
    [InlineData(0.001)]
    [InlineData(0.5)]
    [InlineData(1.0)]
    public void Validate_Succeeds_ForRatiosInTheOpenClosedUnitInterval(double ratio)
    {
        var options = new WorkerTelemetryOptions
        {
            Enabled = true,
            OtlpEndpoint = "http://localhost:4317",
            TracesSamplingRatio = ratio,
        };

        _validator.Validate(name: null, options).Succeeded.ShouldBeTrue();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(1.5)]
    [InlineData(double.NaN)]
    public void Validate_Fails_ForRatiosOutsideTheOpenClosedUnitInterval(double ratio)
    {
        var options = new WorkerTelemetryOptions
        {
            Enabled = true,
            OtlpEndpoint = "http://localhost:4317",
            TracesSamplingRatio = ratio,
        };

        var result = _validator.Validate(name: null, options);

        result.Succeeded.ShouldBeFalse();
        result.Failures.ShouldNotBeNull();
        result.Failures.ShouldContain(
            failure => failure.Contains(nameof(WorkerTelemetryOptions.TracesSamplingRatio), StringComparison.Ordinal));
    }

    /// <summary>A ratio nobody set is not a reason to refuse a start-up.</summary>
    [Fact]
    public void Validate_Succeeds_WhenEnabled_WithTheDefaultSamplingRatio()
    {
        var options = new WorkerTelemetryOptions { Enabled = true, OtlpEndpoint = "http://localhost:4317" };

        _validator.Validate(name: null, options).Succeeded.ShouldBeTrue();
    }

    /// <summary>
    /// A ratio out of range on a host with telemetry switched off still boots — the whole validator
    /// short-circuits on <c>Enabled</c>, exactly as the Api host's does.
    /// </summary>
    [Fact]
    public void Validate_Succeeds_WhenDisabled_EvenWithAnImpossibleSamplingRatio()
    {
        var options = new WorkerTelemetryOptions { Enabled = false, TracesSamplingRatio = -1 };

        _validator.Validate(name: null, options).Succeeded.ShouldBeTrue();
    }

    [Fact]
    public void Validate_RejectsNull() =>
        Should.Throw<ArgumentNullException>(() => _validator.Validate(name: null, options: null!));
}

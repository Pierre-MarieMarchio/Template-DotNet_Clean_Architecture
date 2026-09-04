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

    [Fact]
    public void Validate_RejectsNull() =>
        Should.Throw<ArgumentNullException>(() => _validator.Validate(name: null, options: null!));
}

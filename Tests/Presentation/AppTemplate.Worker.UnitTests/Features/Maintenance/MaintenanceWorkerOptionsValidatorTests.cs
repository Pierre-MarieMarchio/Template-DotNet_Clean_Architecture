using AppTemplate.Worker.Features.Maintenance;
using Shouldly;
using Xunit;

namespace AppTemplate.Worker.UnitTests.Features.Maintenance;

public sealed class MaintenanceWorkerOptionsValidatorTests
{
    private readonly MaintenanceWorkerOptionsValidator _validator = new();

    [Fact]
    public void Validate_Succeeds_ForTheDefaults()
    {
        var result = _validator.Validate(name: null, new MaintenanceWorkerOptions());

        result.Succeeded.ShouldBeTrue();
    }

    [Theory]
    [InlineData("00:00:01")]
    [InlineData("1.00:00:00")]
    public void Validate_Succeeds_AtEachBoundary(string interval)
    {
        var options = new MaintenanceWorkerOptions { Interval = TimeSpan.Parse(interval) };

        _validator.Validate(name: null, options).Succeeded.ShouldBeTrue();
    }

    [Fact]
    public void Validate_Fails_WhenIntervalIsZero()
    {
        var result = _validator.Validate(name: null, new MaintenanceWorkerOptions { Interval = TimeSpan.Zero });

        result.Failed.ShouldBeTrue();
        result.FailureMessage.ShouldContain("MaintenanceWorker:Interval");
    }

    [Fact]
    public void Validate_Fails_WhenIntervalIsNegative()
    {
        var options = new MaintenanceWorkerOptions { Interval = TimeSpan.FromSeconds(-1) };

        _validator.Validate(name: null, options).Failed.ShouldBeTrue();
    }

    [Fact]
    public void Validate_Fails_WhenIntervalExceedsOneDay()
    {
        var options = new MaintenanceWorkerOptions { Interval = TimeSpan.FromDays(1) + TimeSpan.FromSeconds(1) };

        _validator.Validate(name: null, options).Failed.ShouldBeTrue();
    }

    [Fact]
    public void Validate_RejectsNull() =>
        Should.Throw<ArgumentNullException>(() => _validator.Validate(name: null, options: null!));
}

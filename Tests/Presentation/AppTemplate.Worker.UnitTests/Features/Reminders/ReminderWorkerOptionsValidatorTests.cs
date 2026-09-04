using AppTemplate.Worker.Features.Reminders;
using Shouldly;
using Xunit;

namespace AppTemplate.Worker.UnitTests.Features.Reminders;

public sealed class ReminderWorkerOptionsValidatorTests
{
    private readonly ReminderWorkerOptionsValidator _validator = new();

    [Fact]
    public void Validate_Succeeds_ForTheDefaults()
    {
        var result = _validator.Validate(name: null, new ReminderWorkerOptions());

        result.Succeeded.ShouldBeTrue();
    }

    [Theory]
    [InlineData("00:00:01")]
    [InlineData("01:00:00")]
    public void Validate_Succeeds_AtEachBoundary(string interval)
    {
        var options = new ReminderWorkerOptions { Interval = TimeSpan.Parse(interval) };

        _validator.Validate(name: null, options).Succeeded.ShouldBeTrue();
    }

    [Fact]
    public void Validate_Fails_WhenIntervalIsZero()
    {
        var result = _validator.Validate(name: null, new ReminderWorkerOptions { Interval = TimeSpan.Zero });

        result.Failed.ShouldBeTrue();
        result.FailureMessage.ShouldContain("ReminderWorker:Interval");
    }

    [Fact]
    public void Validate_Fails_WhenIntervalIsNegative()
    {
        var options = new ReminderWorkerOptions { Interval = TimeSpan.FromSeconds(-1) };

        _validator.Validate(name: null, options).Failed.ShouldBeTrue();
    }

    [Fact]
    public void Validate_Fails_WhenIntervalExceedsOneHour()
    {
        var options = new ReminderWorkerOptions { Interval = TimeSpan.FromHours(1) + TimeSpan.FromSeconds(1) };

        _validator.Validate(name: null, options).Failed.ShouldBeTrue();
    }

    [Fact]
    public void Validate_RejectsNull() =>
        Should.Throw<ArgumentNullException>(() => _validator.Validate(name: null, options: null!));
}

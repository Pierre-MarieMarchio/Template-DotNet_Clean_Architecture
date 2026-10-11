using AppTemplate.Api.Features.Reminders.Jobs;
using Shouldly;
using Xunit;

namespace AppTemplate.Api.UnitTests.Features.Reminders.Jobs;

public sealed class JobsRemindersOptionsValidatorTests
{
    private readonly JobsRemindersOptionsValidator _validator = new();

    [Fact]
    public void Validate_Succeeds_ForTheDefaults()
    {
        var result = _validator.Validate(name: null, new JobsRemindersOptions());

        result.Succeeded.ShouldBeTrue();
    }

    [Theory]
    [InlineData("00:00:01")]
    [InlineData("01:00:00")]
    public void Validate_Succeeds_AtEachBoundary(string interval)
    {
        var options = new JobsRemindersOptions { Interval = TimeSpan.Parse(interval) };

        _validator.Validate(name: null, options).Succeeded.ShouldBeTrue();
    }

    [Fact]
    public void Validate_Fails_WhenIntervalIsZero()
    {
        var result = _validator.Validate(name: null, new JobsRemindersOptions { Interval = TimeSpan.Zero });

        result.Failed.ShouldBeTrue();
        result.FailureMessage.ShouldContain("Jobs:Reminders:Interval");
    }

    [Fact]
    public void Validate_Fails_WhenIntervalIsNegative()
    {
        var options = new JobsRemindersOptions { Interval = TimeSpan.FromSeconds(-1) };

        _validator.Validate(name: null, options).Failed.ShouldBeTrue();
    }

    [Fact]
    public void Validate_Fails_WhenIntervalExceedsOneHour()
    {
        var options = new JobsRemindersOptions { Interval = TimeSpan.FromHours(1) + TimeSpan.FromSeconds(1) };

        _validator.Validate(name: null, options).Failed.ShouldBeTrue();
    }

    [Fact]
    public void Validate_RejectsNull() =>
        Should.Throw<ArgumentNullException>(() => _validator.Validate(name: null, options: null!));
}

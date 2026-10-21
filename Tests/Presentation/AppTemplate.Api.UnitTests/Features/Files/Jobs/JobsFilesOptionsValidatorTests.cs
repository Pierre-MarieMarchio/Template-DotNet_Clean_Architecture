using AppTemplate.Api.Features.Files.Jobs;
using Shouldly;
using Xunit;

namespace AppTemplate.Api.UnitTests.Features.Files.Jobs;

public sealed class JobsFilesOptionsValidatorTests
{
    private readonly JobsFilesOptionsValidator _validator = new();

    [Fact]
    public void Validate_Succeeds_ForTheDefaults()
    {
        var result = _validator.Validate(name: null, new JobsFilesOptions());

        result.Succeeded.ShouldBeTrue();
    }

    [Theory]
    [InlineData("00:00:01")]
    [InlineData("1.00:00:00")]
    public void Validate_Succeeds_AtEachPurgeBoundary(string interval)
    {
        var options = new JobsFilesOptions { PurgeAbandonedRegistrationsInterval = TimeSpan.Parse(interval) };

        _validator.Validate(name: null, options).Succeeded.ShouldBeTrue();
    }

    [Theory]
    [InlineData("00:01:00")]
    [InlineData("7.00:00:00")]
    public void Validate_Succeeds_AtEachReclaimBoundary(string interval)
    {
        var options = new JobsFilesOptions { ReclaimOrphanedContentInterval = TimeSpan.Parse(interval) };

        _validator.Validate(name: null, options).Succeeded.ShouldBeTrue();
    }

    [Fact]
    public void Validate_Fails_WhenThePurgeIntervalIsZero()
    {
        var options = new JobsFilesOptions { PurgeAbandonedRegistrationsInterval = TimeSpan.Zero };

        var result = _validator.Validate(name: null, options);

        result.Failed.ShouldBeTrue();
        result.FailureMessage.ShouldContain("Jobs:Files:PurgeAbandonedRegistrationsInterval");
    }

    [Fact]
    public void Validate_Fails_WhenThePurgeIntervalExceedsADay()
    {
        var options = new JobsFilesOptions
        {
            PurgeAbandonedRegistrationsInterval = TimeSpan.FromDays(1) + TimeSpan.FromSeconds(1),
        };

        _validator.Validate(name: null, options).Failed.ShouldBeTrue();
    }

    [Fact]
    public void Validate_Fails_WhenTheReclaimIntervalIsBelowAMinute()
    {
        var options = new JobsFilesOptions { ReclaimOrphanedContentInterval = TimeSpan.FromSeconds(59) };

        var result = _validator.Validate(name: null, options);

        result.Failed.ShouldBeTrue();
        result.FailureMessage.ShouldContain("Jobs:Files:ReclaimOrphanedContentInterval");
    }

    [Fact]
    public void Validate_Fails_WhenTheReclaimIntervalIsNegative()
    {
        var options = new JobsFilesOptions { ReclaimOrphanedContentInterval = TimeSpan.FromSeconds(-1) };

        _validator.Validate(name: null, options).Failed.ShouldBeTrue();
    }

    [Fact]
    public void Validate_Fails_WhenTheReclaimIntervalExceedsAWeek()
    {
        var options = new JobsFilesOptions
        {
            ReclaimOrphanedContentInterval = TimeSpan.FromDays(7) + TimeSpan.FromSeconds(1),
        };

        _validator.Validate(name: null, options).Failed.ShouldBeTrue();
    }
}

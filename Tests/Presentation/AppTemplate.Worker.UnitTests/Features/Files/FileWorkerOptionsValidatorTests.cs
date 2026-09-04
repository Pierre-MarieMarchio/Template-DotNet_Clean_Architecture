using AppTemplate.Worker.Features.Files;
using Shouldly;
using Xunit;

namespace AppTemplate.Worker.UnitTests.Features.Files;

public sealed class FileWorkerOptionsValidatorTests
{
    private readonly FileWorkerOptionsValidator _validator = new();

    [Fact]
    public void Validate_Succeeds_ForTheDefaults()
    {
        var result = _validator.Validate(name: null, new FileWorkerOptions());

        result.Succeeded.ShouldBeTrue();
    }

    [Theory]
    [InlineData("00:00:01")]
    [InlineData("1.00:00:00")]
    public void Validate_Succeeds_AtEachPurgeBoundary(string interval)
    {
        var options = new FileWorkerOptions { PurgeAbandonedRegistrationsInterval = TimeSpan.Parse(interval) };

        _validator.Validate(name: null, options).Succeeded.ShouldBeTrue();
    }

    [Theory]
    [InlineData("00:01:00")]
    [InlineData("7.00:00:00")]
    public void Validate_Succeeds_AtEachReclaimBoundary(string interval)
    {
        var options = new FileWorkerOptions { ReclaimOrphanedContentInterval = TimeSpan.Parse(interval) };

        _validator.Validate(name: null, options).Succeeded.ShouldBeTrue();
    }

    [Fact]
    public void Validate_Fails_WhenThePurgeIntervalIsZero()
    {
        var options = new FileWorkerOptions { PurgeAbandonedRegistrationsInterval = TimeSpan.Zero };

        var result = _validator.Validate(name: null, options);

        result.Failed.ShouldBeTrue();
        result.FailureMessage.ShouldContain("FileWorker:PurgeAbandonedRegistrationsInterval");
    }

    [Fact]
    public void Validate_Fails_WhenThePurgeIntervalExceedsADay()
    {
        var options = new FileWorkerOptions
        {
            PurgeAbandonedRegistrationsInterval = TimeSpan.FromDays(1) + TimeSpan.FromSeconds(1),
        };

        _validator.Validate(name: null, options).Failed.ShouldBeTrue();
    }

    /// <summary>
    /// A pass of the orphan sweep lists the entire object store, so a sub-minute cadence describes a
    /// loop that never idles rather than a schedule anybody chose.
    /// </summary>
    [Fact]
    public void Validate_Fails_WhenTheReclaimIntervalIsBelowAMinute()
    {
        var options = new FileWorkerOptions { ReclaimOrphanedContentInterval = TimeSpan.FromSeconds(59) };

        var result = _validator.Validate(name: null, options);

        result.Failed.ShouldBeTrue();
        result.FailureMessage.ShouldContain("FileWorker:ReclaimOrphanedContentInterval");
    }

    [Fact]
    public void Validate_Fails_WhenTheReclaimIntervalIsNegative()
    {
        var options = new FileWorkerOptions { ReclaimOrphanedContentInterval = TimeSpan.FromSeconds(-1) };

        _validator.Validate(name: null, options).Failed.ShouldBeTrue();
    }

    [Fact]
    public void Validate_Fails_WhenTheReclaimIntervalExceedsAWeek()
    {
        var options = new FileWorkerOptions
        {
            ReclaimOrphanedContentInterval = TimeSpan.FromDays(7) + TimeSpan.FromSeconds(1),
        };

        _validator.Validate(name: null, options).Failed.ShouldBeTrue();
    }

    [Fact]
    public void Validate_RejectsNull() =>
        Should.Throw<ArgumentNullException>(() => _validator.Validate(name: null, options: null!));
}

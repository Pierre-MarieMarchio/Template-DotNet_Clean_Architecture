using AppTemplate.Infrastructure.Auth.Features.SecurityEvents.Configuration.Options;
using Shouldly;
using Xunit;

namespace AppTemplate.Infrastructure.Auth.UnitTests.Features.SecurityEvents.Configuration.Options;

/// <summary>
/// The validator runs under <c>ValidateOnStart</c>, so what it rejects is what stops the process
/// from booting rather than surfacing as a trail that keeps everything, or purges too eagerly,
/// the first time the maintenance timer fires.
/// </summary>
public sealed class SecurityEventLogOptionsValidatorTests
{
    private static readonly SecurityEventLogOptionsValidator _validator = new();

    [Fact]
    public void Validate_AcceptsTheDefaults() =>
        _validator.Validate(name: null, new SecurityEventLogOptions()).Succeeded.ShouldBeTrue();

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(3651)]
    public void Validate_RejectsARetentionOutsideTheAllowedRange(int retentionInDays)
    {
        var options = Valid();
        options.RetentionInDays = retentionInDays;

        var result = _validator.Validate(name: null, options);

        result.Failed.ShouldBeTrue();
        result.FailureMessage.ShouldContain("RetentionInDays");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(100001)]
    public void Validate_RejectsABatchSizeOutsideTheAllowedRange(int purgeBatchSize)
    {
        var options = Valid();
        options.PurgeBatchSize = purgeBatchSize;

        var result = _validator.Validate(name: null, options);

        result.Failed.ShouldBeTrue();
        result.FailureMessage.ShouldContain("PurgeBatchSize");
    }

    private static SecurityEventLogOptions Valid() => new();
}

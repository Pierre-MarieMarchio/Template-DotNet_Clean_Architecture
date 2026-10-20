using AppTemplate.Infrastructure.Auth.Features.SecurityEvents.Configuration.Options;
using Shouldly;
using Xunit;

namespace AppTemplate.Infrastructure.Auth.UnitTests.Features.SecurityEvents.Configuration.Options;

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

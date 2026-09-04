using AppTemplate.Infrastructure.Identity.TwoFactor;
using Shouldly;
using Xunit;

namespace AppTemplate.Infrastructure.Identity.UnitTests.TwoFactor;

/// <summary>
/// The validator runs under <c>ValidateOnStart</c>, so what it rejects is what stops the process from
/// booting rather than surfacing as a broken login flow the first time someone tries it.
/// </summary>
public sealed class TwoFactorOptionsValidatorTests
{
    private static readonly TwoFactorOptionsValidator _validator = new();

    [Fact]
    public void Validate_AcceptsTheDefaults() =>
        _validator.Validate(name: null, new TwoFactorOptions()).Succeeded.ShouldBeTrue();

    [Theory]
    [InlineData("00:00:30")]
    [InlineData("00:31:00")]
    public void Validate_RejectsAChallengeLifetimeOutsideTheAllowedRange(string lifetime)
    {
        var options = Valid();
        options.ChallengeLifetime = TimeSpan.Parse(lifetime);

        _validator.Validate(name: null, options).Failed.ShouldBeTrue();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(21)]
    public void Validate_RejectsARecoveryCodeCountOutsideTheAllowedRange(int count)
    {
        var options = Valid();
        options.RecoveryCodeCount = count;

        _validator.Validate(name: null, options).Failed.ShouldBeTrue();
    }

    [Fact]
    public void Validate_RejectsABlankIssuer()
    {
        var options = Valid();
        options.Issuer = "   ";

        var result = _validator.Validate(name: null, options);

        result.Failed.ShouldBeTrue();
        result.FailureMessage.ShouldContain("Issuer");
    }

    private static TwoFactorOptions Valid() => new();
}

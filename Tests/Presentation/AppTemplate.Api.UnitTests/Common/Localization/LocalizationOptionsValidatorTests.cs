using AppTemplate.Api.Common.Localization;
using Shouldly;
using Xunit;

namespace AppTemplate.Api.UnitTests.Common.Localization;

/// <summary>
/// The one key this section has, and the reason it is validated at all: a culture nothing can match
/// would not fail — it would quietly send every mail in the fallback language for the life of the
/// deployment, which is the kind of defect that is only ever noticed by a reader who never
/// complains.
/// </summary>
public sealed class LocalizationOptionsValidatorTests
{
    private readonly LocalizationOptionsValidator _validator = new();

    [Theory]
    [InlineData("en")]
    [InlineData("fr")]
    [InlineData("fr-CA")]
    [InlineData("zh-Hans")]
    public void Validate_Succeeds_ForAWellFormedTag(string culture) =>
        _validator.Validate(name: null, new LocalizationOptions { DefaultCulture = culture })
            .Succeeded.ShouldBeTrue();

    /// <summary>The default is what a host uses when nobody configures the section.</summary>
    [Fact]
    public void Validate_Succeeds_ForTheDefault() =>
        _validator.Validate(name: null, new LocalizationOptions()).Succeeded.ShouldBeTrue();

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_Fails_WhenTheCultureIsBlank(string culture)
    {
        var result = _validator.Validate(name: null, new LocalizationOptions { DefaultCulture = culture });

        result.Failed.ShouldBeTrue();
        result.FailureMessage.ShouldContain(nameof(LocalizationOptions.DefaultCulture));
    }

    /// <summary>
    /// Shape only, and deliberately: this repository builds with <c>InvariantGlobalization</c>, so
    /// the runtime knows no culture but the invariant one and cannot be asked whether 'fr' is real.
    /// </summary>
    [Theory]
    [InlineData("not a tag")]
    [InlineData("e")]
    [InlineData("fr_CA")]
    [InlineData("fr-")]
    [InlineData("*")]
    [InlineData("../../etc/passwd")]
    public void Validate_Fails_ForATagThatIsNotOne(string culture)
    {
        var result = _validator.Validate(name: null, new LocalizationOptions { DefaultCulture = culture });

        result.Failed.ShouldBeTrue();
        result.FailureMessage.ShouldContain(culture);
    }

    [Fact]
    public void Validate_RejectsNull() =>
        Should.Throw<ArgumentNullException>(() => _validator.Validate(name: null, options: null!));

    /// <summary>
    /// The section name is the contract with whoever deploys this, and both hosts bind the same one:
    /// a divergence here would let one of them read a key the other ignores.
    /// </summary>
    [Fact]
    public void TheSectionName_IsTheOneBothHostsBind() =>
        LocalizationOptions.SectionName.ShouldBe("Localization");
}

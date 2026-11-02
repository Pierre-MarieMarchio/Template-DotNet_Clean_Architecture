using AppTemplate.Presentation.Core.Common.Localization;
using Shouldly;
using Xunit;

namespace AppTemplate.Presentation.Core.UnitTests.Common.Localization;

public sealed class LocalizationOptionsValidatorTests
{
    private readonly LocalizationOptionsValidator _validator = new();

    [Theory]
    [InlineData("en")]
    [InlineData("fr-CA")]
    [InlineData("zh-Hans")]
    public void Validate_Succeeds_ForAWellFormedTag(string culture) =>
        _validator.Validate(name: null, new LocalizationOptions { DefaultCulture = culture })
            .Succeeded.ShouldBeTrue();

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
}

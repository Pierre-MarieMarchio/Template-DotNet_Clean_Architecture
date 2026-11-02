using AppTemplate.Api.Common.Security;
using Shouldly;
using Xunit;

namespace AppTemplate.Api.UnitTests.Common.Security;

public sealed class CorsOptionsValidatorTests
{
    private readonly CorsOptionsValidator _validator = new();

    [Fact]
    public void Validate_Succeeds_ForTheDefaults() => _validator.Validate(name: null, new CorsOptions()).Succeeded.ShouldBeTrue();

    [Theory]
    [InlineData("https://app.example")]
    [InlineData("http://localhost:4200")]
    [InlineData("https://app.example:8443")]
    [InlineData("*")]
    public void Validate_Succeeds_ForAWellFormedOrigin(string origin)
    {
        var options = new CorsOptions { AllowedOrigins = { origin } };

        _validator.Validate(name: null, options).Succeeded.ShouldBeTrue();
    }

    [Theory]
    [InlineData("https://app.example/")]
    [InlineData("https://app.example/path")]
    [InlineData("https://app.example?x=1")]
    [InlineData("app.example")]
    [InlineData("ftp://app.example")]
    [InlineData("")]
    public void Validate_Fails_ForAnEntryThatIsNotAnOrigin(string origin)
    {
        var options = new CorsOptions { AllowedOrigins = { "https://app.example", origin } };

        var result = _validator.Validate(name: null, options);

        result.Failed.ShouldBeTrue();
        result.FailureMessage.ShouldContain("Cors:AllowedOrigins");
        result.FailureMessage.ShouldContain($"'{origin}'");
    }
}

using AppTemplate.Infrastructure.Auth.Features.Auth.Options;
using Microsoft.Extensions.Options;
using Shouldly;
using Xunit;

namespace AppTemplate.Infrastructure.Auth.UnitTests.Features.Auth.Options;

public sealed class PasskeyOptionsValidatorTests
{
    private readonly PasskeyOptionsValidator _validator = new();

    [Fact]
    public void AnAbsentServerDomain_IsAccepted()
    {
        Validate(new PasskeyOptions()).Succeeded.ShouldBeTrue();
    }

    [Theory]
    [InlineData("api.example.org")]
    [InlineData("example.org")]
    public void ABareDomain_IsAccepted(string domain)
    {
        Validate(new PasskeyOptions { ServerDomain = domain }).Succeeded.ShouldBeTrue();
    }

    [Theory]
    [InlineData("https://api.example.org")]
    [InlineData("api.example.org:5000")]
    [InlineData("api.example.org/auth")]
    [InlineData("api example.org")]
    [InlineData(" ")]
    [InlineData("")]
    public void AnythingThatIsNotABareDomain_IsRefused(string domain)
    {
        var result = Validate(new PasskeyOptions { ServerDomain = domain });

        result.Succeeded.ShouldBeFalse();
        result.FailureMessage!.ShouldContain("Passkey:ServerDomain");
    }

    private ValidateOptionsResult Validate(PasskeyOptions options) =>
        _validator.Validate(name: null, options);

    private sealed class Never
    {
    }
}

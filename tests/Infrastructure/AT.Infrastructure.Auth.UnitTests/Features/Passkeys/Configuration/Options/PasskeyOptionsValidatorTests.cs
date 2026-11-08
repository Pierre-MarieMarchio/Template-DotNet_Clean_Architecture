using System.Globalization;
using AppTemplate.Infrastructure.Auth.Features.Passkeys.Configuration.Options;
using Microsoft.Extensions.Options;
using Shouldly;
using Xunit;

namespace AppTemplate.Infrastructure.Auth.UnitTests.Features.Passkeys.Configuration.Options;

public sealed class PasskeyOptionsValidatorTests
{
    private readonly PasskeyOptionsValidator _validator = new();

    [Fact]
    public void AnAbsentServerDomain_IsAccepted() => Validate(new PasskeyOptions()).Succeeded.ShouldBeTrue();

    [Theory]
    [InlineData("api.example.org")]
    [InlineData("example.org")]
    public void ABareDomain_IsAccepted(string domain) => Validate(new PasskeyOptions { ServerDomain = domain }).Succeeded.ShouldBeTrue();

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

    [Theory]
    [InlineData("00:01:00")]
    [InlineData("00:30:00")]
    public void ACeremonyLifetimeWithinItsBounds_IsAccepted(string lifetime)
    {
        Validate(new PasskeyOptions { CeremonyLifetime = TimeSpan.Parse(lifetime, CultureInfo.InvariantCulture) })
            .Succeeded.ShouldBeTrue();
    }

    [Theory]
    [InlineData("00:00:00")]
    [InlineData("00:00:59")]
    [InlineData("00:30:01")]
    public void ACeremonyLifetimeOutsideItsBounds_IsRefused(string lifetime)
    {
        var result = Validate(
            new PasskeyOptions { CeremonyLifetime = TimeSpan.Parse(lifetime, CultureInfo.InvariantCulture) });

        result.Succeeded.ShouldBeFalse();
        result.FailureMessage!.ShouldContain("Passkey:CeremonyLifetime");
    }

    private ValidateOptionsResult Validate(PasskeyOptions options) =>
        _validator.Validate(name: null, options);

    private sealed class Never
    {
    }
}

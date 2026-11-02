using System.Text;
using AppTemplate.Infrastructure.Auth.Features.Sessions.Configuration.Options;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Shouldly;
using Xunit;

namespace AppTemplate.Infrastructure.Auth.UnitTests.Features.Sessions.Configuration.Options;

public sealed class JwtOptionsValidatorTests
{
    private const string _shippedDevelopmentKey = "dev-only-signing-key-not-for-production-replace-me-0123456789ab";

    private static readonly JwtOptionsValidator _validator = new(new HostEnvironment(Environments.Development));

    [Fact]
    public void Validate_RejectsAKeyShorterThanTheEnforcedFloor()
    {
        string tooShort = new('k', JwtOptions.MinimumKeyLengthInBytes - 1);
        Encoding.UTF8.GetByteCount(tooShort).ShouldBeLessThan(JwtOptions.MinimumKeyLengthInBytes);

        var result = _validator.Validate(name: null, Valid(tooShort));

        result.Failed.ShouldBeTrue();
        result.FailureMessage.ShouldContain($"{JwtOptions.MinimumKeyLengthInBytes} bytes");
    }

    [Fact]
    public void Validate_CountsTheKeyInBytesRatherThanCharacters()
    {
        string thirtyTwoCharacters = new('k', JwtOptions.MinimumKeyLengthInBytes);

        _validator.Validate(name: null, Valid(thirtyTwoCharacters)).Succeeded.ShouldBeTrue();
        _validator.Validate(name: null, Valid(new string('k', JwtOptions.MinimumKeyLengthInBytes - 1)))
            .Failed.ShouldBeTrue();
    }

    [Fact]
    public void Validate_RejectsAMissingKey() =>
        _validator.Validate(name: null, Valid(key: "   ")).Failed.ShouldBeTrue();

    [Fact]
    public void Validate_RefusesTheShippedDevelopmentKeyInProduction()
    {
        var result = new JwtOptionsValidator(new HostEnvironment(Environments.Production))
            .Validate(name: null, Valid(_shippedDevelopmentKey));

        result.Failed.ShouldBeTrue();
        result.FailureMessage.ShouldContain($"{JwtOptions.SectionName}:Key");
        result.FailureMessage.ShouldContain("Jwt__Key");
    }

    [Fact]
    public void Validate_AcceptsTheShippedDevelopmentKeyInDevelopment() =>
        _validator.Validate(name: null, Valid(_shippedDevelopmentKey)).Succeeded.ShouldBeTrue();

    private static JwtOptions Valid(string key) => new()
    {
        Key = key,
        Issuer = "https://localhost/app-template",
        Audience = "app-template-api",
        AccessTokenLifetimeInMinutes = 15,
    };

    private sealed class HostEnvironment(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;

        public string ApplicationName { get; set; } = "AppTemplate.Infrastructure.Auth.UnitTests";

        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}

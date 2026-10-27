using AppTemplate.PresentationApp.Common.Hosting;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;
using Xunit;

namespace AppTemplate.PresentationApp.UnitTests.Common.Hosting;

public sealed class DataProtectionOptionsValidatorTests
{
    [Fact]
    public void NoKeysDirectory_IsRefusedOutsideDevelopment() =>
        Validate(Environments.Production, keysDirectory: null).Failed.ShouldBeTrue();

    [Fact]
    public void NoKeysDirectory_IsAcceptedInDevelopment() =>
        Validate(Environments.Development, keysDirectory: null).Succeeded.ShouldBeTrue();

    [Fact]
    public void ARelativeKeysDirectory_IsRefused() =>
        Validate(Environments.Production, keysDirectory: "keys").Failed.ShouldBeTrue();

    [Fact]
    public void AnAbsoluteKeysDirectory_IsAccepted() =>
        Validate(Environments.Production, keysDirectory: "/data-protection-keys").Succeeded.ShouldBeTrue();

    private static ValidateOptionsResult Validate(string environment, string? keysDirectory)
    {
        var host = Substitute.For<IHostEnvironment>();
        host.EnvironmentName.Returns(environment);

        return new DataProtectionOptionsValidator(host)
            .Validate(null, new DataProtectionOptions { KeysDirectory = keysDirectory });
    }
}

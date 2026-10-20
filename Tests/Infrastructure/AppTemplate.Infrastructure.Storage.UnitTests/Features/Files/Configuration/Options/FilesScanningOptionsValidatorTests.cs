using AppTemplate.Infrastructure.Storage.Features.Files.Configuration.Options;
using Microsoft.Extensions.Options;
using Shouldly;
using Xunit;

namespace AppTemplate.Infrastructure.Storage.UnitTests.Features.Files.Configuration.Options;

public sealed class FilesScanningOptionsValidatorTests
{
    private readonly FilesScanningOptionsValidator _validator = new();

    [Fact]
    public void AConfigurationWithScanningDisabled_IsValid() =>
        Validate(new FilesScanningOptions()).Succeeded.ShouldBeTrue();

    [Fact]
    public void AnEnabledScannerWithAHost_IsValid() =>
        Validate(new FilesScanningOptions { Enabled = true, Host = "clamav", Port = 3310 })
            .Succeeded.ShouldBeTrue();

    [Fact]
    public void AnEnabledScannerWithNoHost_IsRefused()
    {
        var result = Validate(new FilesScanningOptions { Enabled = true });

        result.Failed.ShouldBeTrue();
        result.FailureMessage.ShouldContain("Files:Scanning:Host");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(65_536)]
    public void APortOutsideItsRange_IsRefused(int port)
    {
        var result = Validate(new FilesScanningOptions { Enabled = true, Host = "clamav", Port = port });

        result.Failed.ShouldBeTrue();
        result.FailureMessage.ShouldContain("Files:Scanning:Port");
    }

    [Fact]
    public void APortNothingWillDial_IsNotJudged() =>
        Validate(new FilesScanningOptions { Port = 0 }).Succeeded.ShouldBeTrue();

    [Fact]
    public void AStreamCeilingBelowAnyRealFile_IsRefused()
    {
        var result = Validate(new FilesScanningOptions { MaxScannableBytes = 1024 });

        result.Failed.ShouldBeTrue();
        result.FailureMessage.ShouldContain("MaxScannableBytes");
    }

    [Fact]
    public void TheDefaultStreamCeiling_MatchesWhatTheDaemonAcceptsOutOfTheBox() =>
        new FilesScanningOptions().MaxScannableBytes.ShouldBe(25L * 1024 * 1024);

    [Fact]
    public void ANullOptions_IsARejectedArgument() =>
        Should.Throw<ArgumentNullException>(() => _validator.Validate(null, null!));

    private ValidateOptionsResult Validate(FilesScanningOptions options) =>
        _validator.Validate(FilesScanningOptions.SectionName, options);
}

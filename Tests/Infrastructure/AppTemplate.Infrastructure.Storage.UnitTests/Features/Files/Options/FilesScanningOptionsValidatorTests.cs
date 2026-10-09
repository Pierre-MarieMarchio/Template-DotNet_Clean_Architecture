using AppTemplate.Infrastructure.Storage.Features.Files.Options;
using Microsoft.Extensions.Options;
using Shouldly;
using Xunit;

namespace AppTemplate.Infrastructure.Storage.UnitTests.Features.Files.Options;

public sealed class FilesScanningOptionsValidatorTests
{
    private readonly FilesScanningOptionsValidator _validator = new();

    /// <summary>
    /// <b>The shipped state, asserted.</b> A deployment that configures nothing here has to start:
    /// the type check needs no scanner.
    /// </summary>
    [Fact]
    public void AConfigurationWithScanningDisabled_IsValid() =>
        Validate(new FilesScanningOptions()).Succeeded.ShouldBeTrue();

    [Fact]
    public void AnEnabledScannerWithAHost_IsValid() =>
        Validate(new FilesScanningOptions { Enabled = true, Host = "clamav", Port = 3310 })
            .Succeeded.ShouldBeTrue();

    /// <summary>
    /// Enabled with nowhere to connect would leave every deposited file waiting for a verdict that
    /// never comes, reported by users rather than at start-up.
    /// </summary>
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

    /// <summary>A port nothing will dial is not judged: a disabled deployment leaves it at a default.</summary>
    [Fact]
    public void APortNothingWillDial_IsNotJudged() =>
        Validate(new FilesScanningOptions { Port = 0 }).Succeeded.ShouldBeTrue();

    /// <summary>
    /// Below a megabyte every real upload would come back as not inspectable, and the policy refuses
    /// those: a system that quarantines everything.
    /// </summary>
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

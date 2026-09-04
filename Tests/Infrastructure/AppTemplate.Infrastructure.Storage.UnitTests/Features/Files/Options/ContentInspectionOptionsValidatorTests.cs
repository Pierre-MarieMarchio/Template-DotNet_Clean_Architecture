using AppTemplate.Infrastructure.Storage.Features.Files.Options;
using Microsoft.Extensions.Options;
using Shouldly;
using Xunit;

namespace AppTemplate.Infrastructure.Storage.UnitTests.Features.Files.Options;

public sealed class ContentInspectionOptionsValidatorTests
{
    private readonly ContentInspectionOptionsValidator _validator = new();

    /// <summary>
    /// <b>The shipped state, asserted.</b> A deployment that configures nothing here has to start:
    /// the content check needs no scanner, and refusing to boot without an antivirus daemon would be
    /// a stronger demand than the one made of the object store, whose credentials are also allowed to
    /// be absent.
    /// </summary>
    [Fact]
    public void AConfigurationWithNoScanner_IsValid() =>
        Validate(new ContentInspectionOptions()).Succeeded.ShouldBeTrue();

    [Fact]
    public void AConfiguredScanner_IsValid() =>
        Validate(new ContentInspectionOptions { ScannerHost = "clamav", ScannerPort = 3310 })
            .Succeeded.ShouldBeTrue();

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(65_536)]
    public void APortOutsideItsRange_IsRefused(int port)
    {
        var result = Validate(new ContentInspectionOptions { ScannerHost = "clamav", ScannerPort = port });

        result.Failed.ShouldBeTrue();
        result.FailureMessage.ShouldContain("ScannerPort");
    }

    /// <summary>
    /// The port is only checked when a host is set. A deployment with no scanner leaves both at their
    /// defaults, and a validator that judged a value nothing will ever use would fail the boot of
    /// every such deployment — the failure mode this rule exists to avoid, not to cause.
    /// </summary>
    [Fact]
    public void APortNothingWillDial_IsNotJudged() =>
        Validate(new ContentInspectionOptions { ScannerPort = 0 }).Succeeded.ShouldBeTrue();

    /// <summary>
    /// Below a megabyte the ceiling is smaller than a photograph, so every real upload would come
    /// back as content nothing can examine — and the policy refuses those. The symptom would be a
    /// system that quarantines everything, reported by users rather than at start-up.
    /// </summary>
    [Fact]
    public void AStreamCeilingBelowAnyRealFile_IsRefused()
    {
        var result = Validate(new ContentInspectionOptions { MaxScannableBytes = 1024 });

        result.Failed.ShouldBeTrue();
        result.FailureMessage.ShouldContain("MaxScannableBytes");
    }

    [Fact]
    public void TheDefaultStreamCeiling_MatchesWhatTheDaemonAcceptsOutOfTheBox() =>
        new ContentInspectionOptions().MaxScannableBytes.ShouldBe(25L * 1024 * 1024);

    [Fact]
    public void ANullOptions_IsARejectedArgument() =>
        Should.Throw<ArgumentNullException>(() => _validator.Validate(null, null!));

    private ValidateOptionsResult Validate(ContentInspectionOptions options) =>
        _validator.Validate(ContentInspectionOptions.SectionName, options);
}

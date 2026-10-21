using AppTemplate.Infrastructure.Email.Common.Smtp;
using MailKit.Security;
using Shouldly;
using Xunit;

namespace AppTemplate.Infrastructure.Email.UnitTests.Common.Smtp;

public sealed class EmailOptionsValidatorTests
{
    private static readonly SecureSocketOptions[] _downgradableModes =
    [
        SecureSocketOptions.None,
        SecureSocketOptions.Auto,
        SecureSocketOptions.StartTlsWhenAvailable,
    ];

    private static readonly EmailOptionsValidator _validator = new();

    public static TheoryData<SecureSocketOptions> DowngradableModes => [.. _downgradableModes];

    public static TheoryData<string> LoopbackHosts => ["localhost", "127.0.0.1", "::1", "[::1]"];

    [Fact]
    public void Validate_AcceptsARelayConfiguredForMandatoryStartTls() =>
        _validator.Validate(name: null, Valid()).Succeeded.ShouldBeTrue();

    [Theory]
    [InlineData(SecureSocketOptions.StartTls)]
    [InlineData(SecureSocketOptions.SslOnConnect)]
    public void Validate_AcceptsAModeThatCannotFallBackToPlaintext(SecureSocketOptions security)
    {
        var options = Valid();
        options.Security = security;

        _validator.Validate(name: null, options).Succeeded.ShouldBeTrue();
    }

    [Theory]
    [MemberData(nameof(DowngradableModes))]
    public void Validate_RejectsADowngradableModeForANonLoopbackHost(SecureSocketOptions security)
    {
        var options = Valid();
        options.Security = security;

        string message = RejectionMessageFor(options);

        message.ShouldContain("plaintext");
        message.ShouldContain(security.ToString());
    }

    [Theory]
    [MemberData(nameof(LoopbackHosts))]
    public void Validate_AcceptsADowngradableModeForALoopbackHost(string host)
    {
        var options = Valid();
        options.Host = host;
        options.Security = SecureSocketOptions.None;

        _validator.Validate(name: null, options).Succeeded.ShouldBeTrue();
    }

    [Fact]
    public void Validate_RecognisesALoopbackHostWhateverItsCasing()
    {
        var options = Valid();
        options.Host = "LocalHost";
        options.Security = SecureSocketOptions.None;

        _validator.Validate(name: null, options).Succeeded.ShouldBeTrue();
    }

    [Fact]
    public void Validate_AcceptsADowngradableModeWhenInsecureTransportIsAllowedByName()
    {
        var options = Valid();
        options.Security = SecureSocketOptions.None;
        options.AllowInsecureTransport = true;

        _validator.Validate(name: null, options).Succeeded.ShouldBeTrue();
    }

    [Theory]
    [InlineData(587)]
    [InlineData(465)]
    public void Validate_TreatsAutoAsADowngradeWhateverThePort(int port)
    {
        var options = Valid();
        options.Security = SecureSocketOptions.Auto;
        options.Port = port;

        RejectionMessageFor(options).ShouldContain(nameof(SecureSocketOptions.Auto));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(65536)]
    public void Validate_RejectsAPortOutsideTheAddressableRange(int port)
    {
        var options = Valid();
        options.Port = port;

        RejectionMessageFor(options).ShouldContain("Port");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(65535)]
    public void Validate_AcceptsThePortsAtTheEdgesOfTheRange(int port)
    {
        var options = Valid();
        options.Port = port;

        _validator.Validate(name: null, options).Succeeded.ShouldBeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_RejectsABlankHost(string host)
    {
        var options = Valid();
        options.Host = host;

        RejectionMessageFor(options).ShouldContain("Host");
    }

    [Fact]
    public void Validate_DoesNotTreatABlankHostAsLoopback()
    {
        var options = Valid();
        options.Host = "";
        options.Security = SecureSocketOptions.None;

        var failures = RejectionsFor(options);

        failures.Count.ShouldBe(2);
        failures.ShouldContain(failure => failure.Contains("plaintext", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_RejectsABlankFromAddress(string fromAddress)
    {
        var options = Valid();
        options.FromAddress = fromAddress;

        RejectionMessageFor(options).ShouldContain("required");
    }

    [Theory]
    [InlineData("no-reply@")]
    [InlineData("@example.invalid")]
    [InlineData("no-reply@one@two.invalid")]
    [InlineData("no reply@example.invalid")]
    public void Validate_RejectsAFromAddressThatIsNotAnAddress(string fromAddress)
    {
        var options = Valid();
        options.FromAddress = fromAddress;

        RejectionMessageFor(options).ShouldContain("not a valid email address");
    }

    [Fact]
    public void Validate_AcceptsAFromAddressWithNoDomainBecauseTheParserReadsItAsAMailbox()
    {
        var options = Valid();
        options.FromAddress = "no-reply";

        _validator.Validate(name: null, options).Succeeded.ShouldBeTrue();
    }

    [Fact]
    public void Validate_ReportsEveryFailureFromASinglePass()
    {
        var options = new EmailOptions
        {
            Host = "   ",
            Port = 0,
            FromAddress = "@example.invalid",
            Security = SecureSocketOptions.None,
        };

        var failures = RejectionsFor(options);

        failures.Count.ShouldBe(4);
        failures.ShouldContain(failure => failure.Contains("Host", StringComparison.Ordinal));
        failures.ShouldContain(failure => failure.Contains("Port", StringComparison.Ordinal));
        failures.ShouldContain(failure => failure.Contains("FromAddress", StringComparison.Ordinal));
        failures.ShouldContain(failure => failure.Contains("plaintext", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_NamesTheConfigurationSectionInEveryFailure()
    {
        var options = new EmailOptions { Host = "", Port = 0, FromAddress = "" };

        RejectionsFor(options).ShouldAllBe(failure => failure.Contains("Email:", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_AsksForNoRelayWhenTheTransportIsNotSmtp()
    {
        var options = new EmailOptions
        {
            Transport = EmailOptions.PostmarkTransport,
            FromAddress = "no-reply@example.invalid",
            FromName = "AppTemplate",
        };

        _validator.Validate(name: null, options).Succeeded.ShouldBeTrue();
    }

    [Fact]
    public void Validate_StillRequiresAFromAddressWhenTheTransportIsNotSmtp()
    {
        var options = new EmailOptions { Transport = EmailOptions.PostmarkTransport, FromAddress = "" };

        RejectionMessageFor(options).ShouldContain("FromAddress");
    }

    [Theory]
    [InlineData("smtp")]
    [InlineData("SMTP")]
    public void Validate_RecognisesTheSmtpTransportWhateverItsCasing(string transport)
    {
        var options = Valid();
        options.Transport = transport;
        options.Host = "";

        RejectionMessageFor(options).ShouldContain("Host");
    }

    [Theory]
    [InlineData("Postmrak")]
    [InlineData("SendGrid")]
    [InlineData("")]
    public void Validate_RejectsATransportItDoesNotRecognise(string transport)
    {
        var options = Valid();
        options.Transport = transport;

        string message = RejectionMessageFor(options);

        message.ShouldContain("Transport");
        message.ShouldContain(EmailOptions.SmtpTransport);
        message.ShouldContain(EmailOptions.PostmarkTransport);
    }

    [Fact]
    public void Validate_ReportsAnUnrecognisedTransportWithoutTheRelayFailuresItWouldCause()
    {
        var options = new EmailOptions { Transport = "SendGrid", FromAddress = "no-reply@example.invalid" };

        var failures = RejectionsFor(options);

        failures.ShouldHaveSingleItem().ShouldContain("Transport");
    }

    private static IReadOnlyList<string> RejectionsFor(EmailOptions options)
    {
        var result = _validator.Validate(name: null, options);

        result.Failed.ShouldBeTrue();
        var failures = result.Failures;
        failures.ShouldNotBeNull();

        return [.. failures];
    }

    private static string RejectionMessageFor(EmailOptions options)
    {
        var result = _validator.Validate(name: null, options);

        result.Failed.ShouldBeTrue();
        string message = result.FailureMessage;
        message.ShouldNotBeNull();

        return message;
    }

    private static EmailOptions Valid() => new()
    {
        Host = "smtp.example.invalid",
        Port = 587,
        FromAddress = "no-reply@example.invalid",
        FromName = "AppTemplate",
        Security = SecureSocketOptions.StartTls,
    };
}

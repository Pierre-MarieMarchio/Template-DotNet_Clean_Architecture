using AppTemplate.Infrastructure.Email.Common.Smtp;
using MailKit.Security;
using Shouldly;
using Xunit;

namespace AppTemplate.Infrastructure.Email.UnitTests.Common.Smtp;

/// <summary>
/// The validator runs under <c>ValidateOnStart</c>, so what it rejects is what stops the process from
/// booting. The rule that matters is the transport one: a <see cref="SecureSocketOptions"/> mode able
/// to fall back to plaintext is refused against anything but a loopback host, unless an unencrypted
/// transport is asked for by name. Nothing downstream would notice if that rule were relaxed — the
/// mail still arrives, and it arrives readable on the wire.
/// </summary>
public sealed class EmailOptionsValidatorTests
{
    /// <summary>
    /// The modes MailKit can satisfy without encryption. Enumerated once so that a test asserting on
    /// "every downgradable mode" cannot fall out of step with the validator by listing two of three.
    /// </summary>
    private static readonly SecureSocketOptions[] _downgradableModes =
    [
        SecureSocketOptions.None,
        SecureSocketOptions.Auto,
        SecureSocketOptions.StartTlsWhenAvailable,
    ];

    private static readonly string[] _loopbackHosts = ["localhost", "127.0.0.1", "::1", "[::1]"];

    private static readonly EmailOptionsValidator _validator = new();

    public static TheoryData<SecureSocketOptions> DowngradableModes => [.. _downgradableModes];

    /// <summary>Every mode-and-host pair, so a failing one is named in the test output.</summary>
    public static TheoryData<SecureSocketOptions, string> DowngradableModesAgainstLoopbackHosts
    {
        get
        {
            TheoryData<SecureSocketOptions, string> pairs = [];

            foreach (var security in _downgradableModes)
            {
                foreach (var host in _loopbackHosts)
                {
                    pairs.Add(security, host);
                }
            }

            return pairs;
        }
    }

    [Fact]
    public void Validate_AcceptsARelayConfiguredForMandatoryStartTls()
    {
        _validator.Validate(name: null, Valid()).Succeeded.ShouldBeTrue();
    }

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

        var message = RejectionMessageFor(options);

        message.ShouldContain("plaintext");
        message.ShouldContain(security.ToString());
    }

    /// <summary>
    /// A relay on the same host cannot be reached over a network, so the downgrade the rule guards
    /// against has nowhere to happen. All four spellings of loopback are accepted, including the two
    /// IPv6 ones — a validator that only knew "localhost" would refuse a working compose file and
    /// invite somebody to widen the rule instead of the host list.
    /// </summary>
    [Theory]
    [MemberData(nameof(DowngradableModesAgainstLoopbackHosts))]
    public void Validate_AcceptsADowngradableModeForALoopbackHost(
        SecureSocketOptions security,
        string host)
    {
        var options = Valid();
        options.Host = host;
        options.Security = security;

        _validator.Validate(name: null, options).Succeeded.ShouldBeTrue();
    }

    /// <summary>Hosts arrive from configuration, where casing is nobody's contract.</summary>
    [Fact]
    public void Validate_RecognisesALoopbackHostWhateverItsCasing()
    {
        var options = Valid();
        options.Host = "LocalHost";
        options.Security = SecureSocketOptions.None;

        _validator.Validate(name: null, options).Succeeded.ShouldBeTrue();
    }

    /// <summary>
    /// The escape hatch exists for a development relay that speaks no TLS at all and whose hostname
    /// is a container name rather than loopback. It has to be set deliberately, which is the point:
    /// an unencrypted transport becomes a line in configuration that an auditor can grep for.
    /// </summary>
    [Theory]
    [MemberData(nameof(DowngradableModes))]
    public void Validate_AcceptsADowngradableModeWhenInsecureTransportIsAllowedByName(
        SecureSocketOptions security)
    {
        var options = Valid();
        options.Security = security;
        options.AllowInsecureTransport = true;

        _validator.Validate(name: null, options).Succeeded.ShouldBeTrue();
    }

    /// <summary>
    /// <c>Auto</c> reads like "let MailKit decide", and MailKit decides
    /// <see cref="SecureSocketOptions.StartTlsWhenAvailable"/> on every port but 465 — opportunistic
    /// TLS, which a relay declines by simply not advertising STARTTLS. It therefore belongs with the
    /// two modes it is a synonym for, and the rule is deliberately port-independent: a configuration
    /// that means implicit TLS says <see cref="SecureSocketOptions.SslOnConnect"/>, rather than
    /// leaving the guarantee to be inferred from a port number that a later edit can change.
    /// </summary>
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
    [InlineData(-1)]
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

    /// <summary>
    /// A blank host is not loopback, so it must not slip past the transport rule either: the failure
    /// list has to name both problems rather than treat an absent host as permission.
    /// </summary>
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

    /// <summary>
    /// Where the rule actually stops: MimeKit is the arbiter of what an address is, and it reads a
    /// bare atom as a mailbox with no domain, so <c>no-reply</c> boots. That is a start-up check
    /// passing on something no relay will accept — recorded here because the boundary is invisible
    /// otherwise, and this is the test that would change if the module ever required a domain.
    /// </summary>
    [Fact]
    public void Validate_AcceptsAFromAddressWithNoDomainBecauseTheParserReadsItAsAMailbox()
    {
        var options = Valid();
        options.FromAddress = "no-reply";

        _validator.Validate(name: null, options).Succeeded.ShouldBeTrue();
    }

    /// <summary>
    /// Every problem is reported from one pass. A validator that stopped at the first would turn
    /// fixing a misconfigured relay into one restart per mistake, and the person restarting would
    /// reasonably conclude after the second that the transport warning was the last of them.
    /// </summary>
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

    /// <summary>Each failure names the configuration key it is about, so it can be acted on.</summary>
    [Fact]
    public void Validate_NamesTheConfigurationSectionInEveryFailure()
    {
        var options = new EmailOptions { Host = "", Port = 0, FromAddress = "" };

        RejectionsFor(options).ShouldAllBe(failure => failure.Contains("Email:", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_ThrowsWhenThereAreNoOptionsToValidate()
    {
        Should.Throw<ArgumentNullException>(() => _validator.Validate(name: null, options: null!));
    }

    /// <summary>Asserts the configuration was refused, and hands back the reasons it gave.</summary>
    private static IReadOnlyList<string> RejectionsFor(EmailOptions options)
    {
        var result = _validator.Validate(name: null, options);

        result.Failed.ShouldBeTrue();
        var failures = result.Failures;
        failures.ShouldNotBeNull();

        return [.. failures];
    }

    /// <summary>The same, as the single string a refused start-up prints.</summary>
    private static string RejectionMessageFor(EmailOptions options)
    {
        var result = _validator.Validate(name: null, options);

        result.Failed.ShouldBeTrue();
        var message = result.FailureMessage;
        message.ShouldNotBeNull();

        return message;
    }

    /// <summary>An SMTP relay that would pass every rule, so a test can spoil one field at a time.</summary>
    private static EmailOptions Valid() => new()
    {
        Host = "smtp.example.invalid",
        Port = 587,
        FromAddress = "no-reply@example.invalid",
        FromName = "AppTemplate",
        Security = SecureSocketOptions.StartTls,
    };
}

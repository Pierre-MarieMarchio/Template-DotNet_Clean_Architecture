using AppTemplate.Infrastructure.Email.Common.Http;
using Shouldly;
using Xunit;

namespace AppTemplate.Infrastructure.Email.UnitTests.Common.Http;

/// <summary>
/// This validator runs under <c>ValidateOnStart</c>, so what it rejects is what stops the process
/// from booting. Its transport rule is the counterpart of the SMTP one: there the danger is a mode
/// that falls back to plaintext, here it is a base URL that is plaintext to begin with — and what
/// travels in the clear then is not one message but the credential that sends all of them, on every
/// request.
/// </summary>
public sealed class PostmarkOptionsValidatorTests
{
    private static readonly PostmarkOptionsValidator _validator = new();

    [Fact]
    public void Validate_AcceptsATokenAndTheProvidersOwnEndpoint()
    {
        _validator.Validate(name: null, Valid()).Succeeded.ShouldBeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_RejectsABlankServerToken(string serverToken)
    {
        var options = Valid();
        options.ServerToken = serverToken;

        RejectionMessageFor(options).ShouldContain("ServerToken");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_RejectsABlankMessageStream(string messageStream)
    {
        var options = Valid();
        options.MessageStream = messageStream;

        RejectionMessageFor(options).ShouldContain("MessageStream");
    }

    [Theory]
    [InlineData("api.postmarkapp.com")]
    [InlineData("/email")]
    [InlineData("ftp://api.postmarkapp.com/")]
    [InlineData("")]
    public void Validate_RejectsABaseUrlThatIsNotAnAbsoluteHttpUrl(string apiBaseUrl)
    {
        var options = Valid();
        options.ApiBaseUrl = apiBaseUrl;

        RejectionMessageFor(options).ShouldContain("absolute http or https");
    }

    [Fact]
    public void Validate_RejectsAPlaintextEndpointBecauseTheTokenWouldTravelWithEveryRequest()
    {
        var options = Valid();
        options.ApiBaseUrl = "http://mail-proxy.example.invalid/";

        var message = RejectionMessageFor(options);

        message.ShouldContain("ServerToken");
        message.ShouldContain("readable on the wire");
    }

    /// <summary>
    /// The one exception, for the same reason the SMTP rule makes it: a request that never leaves the
    /// machine cannot be read off a network. It is what lets a local mock stand in for the provider.
    /// </summary>
    [Theory]
    [InlineData("http://localhost:8025/")]
    [InlineData("http://127.0.0.1:8025/")]
    [InlineData("http://[::1]:8025/")]
    public void Validate_AcceptsAPlaintextEndpointOnLoopback(string apiBaseUrl)
    {
        var options = Valid();
        options.ApiBaseUrl = apiBaseUrl;

        _validator.Validate(name: null, options).Succeeded.ShouldBeTrue();
    }

    /// <summary>
    /// One pass reports everything, for the same reason the SMTP validator does: otherwise fixing a
    /// misconfigured transport is one restart per mistake.
    /// </summary>
    [Fact]
    public void Validate_ReportsEveryFailureFromASinglePass()
    {
        var options = new PostmarkOptions
        {
            ServerToken = "",
            MessageStream = "",
            ApiBaseUrl = "not a url",
        };

        var result = _validator.Validate(name: null, options);

        result.Failed.ShouldBeTrue();
        result.Failures.ShouldNotBeNull().Count().ShouldBe(3);
    }

    /// <summary>
    /// No failure may render the credential. This validator is handed the token and reports on it by
    /// name in two of its three rules, which is exactly where quoting the value would look helpful.
    /// </summary>
    [Fact]
    public void Validate_NamesTheServerTokenKeyWithoutQuotingItsValue()
    {
        var options = Valid();
        options.ServerToken = "postmark-server-token-3f9c1a7e";
        options.ApiBaseUrl = "http://mail-proxy.example.invalid/";

        RejectionMessageFor(options).ShouldNotContain(options.ServerToken);
    }

    [Fact]
    public void Validate_NamesTheConfigurationSectionInEveryFailure()
    {
        var options = new PostmarkOptions { ServerToken = "", MessageStream = "", ApiBaseUrl = "" };

        var result = _validator.Validate(name: null, options);

        result.Failures.ShouldNotBeNull()
            .ShouldAllBe(failure => failure.Contains("Postmark:", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_ThrowsWhenThereAreNoOptionsToValidate()
    {
        Should.Throw<ArgumentNullException>(() => _validator.Validate(name: null, options: null!));
    }

    private static string RejectionMessageFor(PostmarkOptions options)
    {
        var result = _validator.Validate(name: null, options);

        result.Failed.ShouldBeTrue();
        var message = result.FailureMessage;
        message.ShouldNotBeNull();

        return message;
    }

    /// <summary>Settings that would pass every rule, so a test can spoil one field at a time.</summary>
    private static PostmarkOptions Valid() => new() { ServerToken = "postmark-server-token-3f9c1a7e" };
}

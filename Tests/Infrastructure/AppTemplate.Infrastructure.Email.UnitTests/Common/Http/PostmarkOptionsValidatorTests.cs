using AppTemplate.Infrastructure.Email.Common.Http;
using Shouldly;
using Xunit;

namespace AppTemplate.Infrastructure.Email.UnitTests.Common.Http;

public sealed class PostmarkOptionsValidatorTests
{
    private static readonly PostmarkOptionsValidator _validator = new();

    [Fact]
    public void Validate_AcceptsATokenAndTheProvidersOwnEndpoint() =>
        _validator.Validate(name: null, Valid()).Succeeded.ShouldBeTrue();

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

        string message = RejectionMessageFor(options);

        message.ShouldContain("ServerToken");
        message.ShouldContain("readable on the wire");
    }

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

    private static string RejectionMessageFor(PostmarkOptions options)
    {
        var result = _validator.Validate(name: null, options);

        result.Failed.ShouldBeTrue();
        string message = result.FailureMessage;
        message.ShouldNotBeNull();

        return message;
    }

    private static PostmarkOptions Valid() => new() { ServerToken = "postmark-server-token-3f9c1a7e" };
}

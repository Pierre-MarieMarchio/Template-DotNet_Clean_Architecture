using AppTemplate.Api.Common.Security;
using Shouldly;
using Xunit;

namespace AppTemplate.Api.UnitTests.Common.Security;

public sealed class ReverseProxyOptionsValidatorTests
{
    private readonly ReverseProxyOptionsValidator _validator = new();

    [Fact]
    public void Validate_Succeeds_ForAProxyAndANetworkThatParse()
    {
        var options = Enabled();
        options.KnownProxies.Add("10.0.0.7");
        options.KnownNetworks.Add("10.0.0.0/8");

        _validator.Validate(name: null, options).Succeeded.ShouldBeTrue();
    }

    [Theory]
    [InlineData("not-an-ip")]
    [InlineData("10.0.0.7/32")]
    [InlineData("")]
    public void Validate_Fails_ForAKnownProxyThatIsNotAnAddress(string proxy)
    {
        var options = Enabled();
        options.KnownProxies.Add(proxy);

        var result = _validator.Validate(name: null, options);

        result.Failed.ShouldBeTrue();
        result.FailureMessage.ShouldContain("ReverseProxy:KnownProxies");
    }

    [Theory]
    [InlineData("10.0.0.0")]
    [InlineData("not-a-network")]
    [InlineData("")]
    public void Validate_Fails_ForAKnownNetworkThatIsNotACidrBlock(string network)
    {
        var options = Enabled();
        options.KnownNetworks.Add(network);

        var result = _validator.Validate(name: null, options);

        result.Failed.ShouldBeTrue();
        result.FailureMessage.ShouldContain("ReverseProxy:KnownNetworks");
    }

    [Theory]
    [InlineData("10.0.0.1/8", "10.0.0.0/8")]
    [InlineData("10.0.0.7/24", "10.0.0.0/24")]
    [InlineData("192.168.1.5/16", "192.168.0.0/16")]
    public void Validate_Fails_ForAPrefixThatDoesNotStartOnItsOwnNetwork(string network, string block)
    {
        var options = Enabled();
        options.KnownNetworks.Add(network);

        string message = _validator.Validate(name: null, options).FailureMessage.ShouldNotBeNull();

        message.ShouldContain(network);
        message.ShouldContain(block, Case.Sensitive, "the refusal has to say what to write instead.");
    }

    [Theory]
    [InlineData("10.0.0.0/8")]
    [InlineData("10.0.0.7/32")]
    [InlineData("192.168.0.0/16")]
    public void Validate_Succeeds_ForAPrefixThatStartsOnItsOwnNetwork(string network)
    {
        var options = Enabled();
        options.KnownNetworks.Add(network);

        _validator.Validate(name: null, options).Succeeded.ShouldBeTrue();
    }

    [Fact]
    public void Validate_NamesEveryMalformedEntry()
    {
        var options = Enabled();
        options.KnownProxies.Add("nonsense");
        options.KnownNetworks.Add("also-nonsense");

        string message = _validator.Validate(name: null, options).FailureMessage.ShouldNotBeNull();

        message.ShouldContain("nonsense");
        message.ShouldContain("also-nonsense");
    }

    [Fact]
    public void Validate_Succeeds_ForAMalformedEntryWhileDisabled()
    {
        var options = new ReverseProxyOptions();
        options.KnownProxies.Add("not-an-ip");

        _validator.Validate(name: null, options).Succeeded.ShouldBeTrue();
    }

    private static ReverseProxyOptions Enabled() => new() { Enabled = true };
}

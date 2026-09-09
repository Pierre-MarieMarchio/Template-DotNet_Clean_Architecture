using AppTemplate.Api.Core.Common.Security;
using Shouldly;
using Xunit;

namespace AppTemplate.Api.Core.UnitTests.Common.Security;

/// <summary>
/// What the validator refuses about the two lists of hops. Both rules exist because the consequence
/// of a wrong entry is silent: a malformed address is one the forwarding middleware never matches,
/// so the deployment believes it trusts its proxy and does not.
/// </summary>
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

    /// <summary>A bare address and a word are both refused, as is nothing at all.</summary>
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

    /// <summary>
    /// The entry that parses and still means something else. <c>IPNetwork.TryParse</c> accepts
    /// <c>10.0.0.7/8</c> and masks it to <c>10.0.0.0/8</c>, so a deployer naming one host with a
    /// short prefix would have trusted sixteen million addresses. The refusal offers the block the
    /// prefix actually names, because that is the entry they either wanted or need to narrow.
    /// </summary>
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

    /// <summary>
    /// The two entries that are their own network: a real block, and a single host as a <c>/32</c>,
    /// which is the narrowest legitimate way to name one proxy.
    /// </summary>
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

    /// <summary>Every malformed entry is named, not just the first: a deployer fixes one round trip.</summary>
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

    /// <summary>A list is only read when forwarding is on, so a disabled deployment is never refused for it.</summary>
    [Fact]
    public void Validate_Succeeds_ForAMalformedEntryWhileDisabled()
    {
        var options = new ReverseProxyOptions();
        options.KnownProxies.Add("not-an-ip");

        _validator.Validate(name: null, options).Succeeded.ShouldBeTrue();
    }

    private static ReverseProxyOptions Enabled() => new() { Enabled = true };
}

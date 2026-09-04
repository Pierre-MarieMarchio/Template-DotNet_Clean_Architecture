using System.Net;
using Microsoft.Extensions.Options;

namespace AppTemplate.Api.Common.Security;

/// <summary>
/// Which upstream hops may rewrite the client's address and scheme.
/// <para>
/// Public because it is bound from configuration and its section name is part of the template's
/// contract with whoever deploys it.
/// </para>
/// </summary>
public sealed class ReverseProxyOptions
{
    public const string SectionName = "ReverseProxy";

    /// <summary>
    /// Off by default. An unconfigured deployment trusts no forwarding header, which costs
    /// per-client rate limiting behind a proxy but never hands a caller the ability to choose its
    /// own rate-limit partition.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>Literal addresses of the immediate proxies, e.g. <c>10.0.0.7</c>.</summary>
    public IList<string> KnownProxies { get; } = [];

    /// <summary>
    /// CIDR blocks of the immediate proxies, e.g. <c>10.0.0.0/8</c>. The address must be the network
    /// address itself: <c>10.0.0.1/8</c> is rejected rather than silently masked to <c>10.0.0.0/8</c>.
    /// </summary>
    public IList<string> KnownNetworks { get; } = [];

    /// <summary>
    /// How many entries to consume from the right of <c>X-Forwarded-For</c>. Must equal the number
    /// of proxies actually in front of the app: a larger value lets a caller prepend a forged hop
    /// and have it read as the client address.
    /// </summary>
    public int ForwardLimit { get; set; } = 1;
}

internal sealed class ReverseProxyOptionsValidator : IValidateOptions<ReverseProxyOptions>
{
    public ValidateOptionsResult Validate(string? name, ReverseProxyOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (!options.Enabled)
        {
            return ValidateOptionsResult.Success;
        }

        var failures = new List<string>();

        // An empty trust set is the one configuration that must not boot. ASP.NET Core's
        // ForwardedHeadersMiddleware only verifies the peer when at least one proxy or network is
        // known; with both lists empty it accepts X-Forwarded-For from anyone, so every caller
        // could pick its own rate-limit partition. That is strictly worse than leaving the
        // middleware out, which is what Enabled=false does.
        if (options.KnownProxies.Count == 0 && options.KnownNetworks.Count == 0)
        {
            failures.Add(
                $"'{ReverseProxyOptions.SectionName}:Enabled' is true but neither " +
                $"'{ReverseProxyOptions.SectionName}:KnownProxies' nor " +
                $"'{ReverseProxyOptions.SectionName}:KnownNetworks' lists an entry. Forwarded headers " +
                "would then be accepted from any caller. List the proxy, or set 'Enabled' to false.");
        }

        foreach (var proxy in options.KnownProxies)
        {
            if (!IPAddress.TryParse(proxy, out _))
            {
                failures.Add(
                    $"'{ReverseProxyOptions.SectionName}:KnownProxies' contains '{proxy}', " +
                    "which is not an IP address.");
            }
        }

        foreach (var network in options.KnownNetworks)
        {
            if (!IPNetwork.TryParse(network, out _))
            {
                failures.Add(
                    $"'{ReverseProxyOptions.SectionName}:KnownNetworks' contains '{network}', which is " +
                    "not a CIDR block whose address is the network address, such as '10.0.0.0/8'.");
            }
        }

        if (options.ForwardLimit < 1)
        {
            failures.Add(
                $"'{ReverseProxyOptions.SectionName}:ForwardLimit' must be at least 1.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}

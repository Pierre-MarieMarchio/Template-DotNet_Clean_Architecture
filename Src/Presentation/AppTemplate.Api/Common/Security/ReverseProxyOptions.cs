using System.Net;
using Microsoft.Extensions.Options;

namespace AppTemplate.Api.Common.Security;

public sealed class ReverseProxyOptions
{
    public const string SectionName = "ReverseProxy";

    public bool Enabled { get; set; }

    public IList<string> KnownProxies { get; } = [];

    public IList<string> KnownNetworks { get; } = [];

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

        if (options.KnownProxies.Count == 0 && options.KnownNetworks.Count == 0)
        {
            failures.Add(
                $"'{ReverseProxyOptions.SectionName}:Enabled' is true but neither " +
                $"'{ReverseProxyOptions.SectionName}:KnownProxies' nor " +
                $"'{ReverseProxyOptions.SectionName}:KnownNetworks' lists an entry. Forwarded headers " +
                "would then be accepted from any caller. List the proxy, or set 'Enabled' to false.");
        }

        foreach (string proxy in options.KnownProxies)
        {
            if (!IPAddress.TryParse(proxy, out _))
            {
                failures.Add(
                    $"'{ReverseProxyOptions.SectionName}:KnownProxies' contains '{proxy}', " +
                    "which is not an IP address.");
            }
        }

        foreach (string network in options.KnownNetworks)
        {
            if (!IPNetwork.TryParse(network, out var parsed))
            {
                failures.Add(
                    $"'{ReverseProxyOptions.SectionName}:KnownNetworks' contains '{network}', which is " +
                    "not a CIDR block whose address is the network address, such as '10.0.0.0/8'.");

                continue;
            }

            if (!AddressIsItsOwnNetwork(network, parsed))
            {
                failures.Add(
                    $"'{ReverseProxyOptions.SectionName}:KnownNetworks' contains '{network}', whose " +
                    $"address is not the network address of its own prefix. Write '{parsed}' to trust " +
                    "that block, or narrow the prefix to the address you meant.");
            }
        }

        if (options.ForwardLimit < 1)
        {
            failures.Add(
                $"'{ReverseProxyOptions.SectionName}:ForwardLimit' must be at least 1.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    private static bool AddressIsItsOwnNetwork(string entry, IPNetwork parsed)
    {
        int slash = entry.IndexOf('/');

        return slash > 0
            && IPAddress.TryParse(entry.AsSpan(0, slash), out var written)
            && written.Equals(parsed.BaseAddress);
    }
}

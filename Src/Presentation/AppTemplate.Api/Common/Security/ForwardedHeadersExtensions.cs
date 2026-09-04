using System.Net;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Options;

// Microsoft.AspNetCore.HttpOverrides also declares IPNetwork, deprecated in favour of this one.
using IPNetwork = System.Net.IPNetwork;

namespace AppTemplate.Api.Common.Security;

/// <summary>
/// Translates <c>X-Forwarded-For</c> and <c>X-Forwarded-Proto</c> into
/// <see cref="HttpContext.Connection"/> and the request scheme, but only for peers named in
/// configuration. The rate limiter partitions on the remote address, so without this every caller
/// behind a proxy shares one window; with it misconfigured, every caller chooses its own.
/// </summary>
public static class ForwardedHeadersExtensions
{
    public static IServiceCollection AddApiForwardedHeaders(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<ReverseProxyOptions>()
            .Bind(configuration.GetSection(ReverseProxyOptions.SectionName))
            .ValidateOnStart();

        services.AddSingleton<IValidateOptions<ReverseProxyOptions>, ReverseProxyOptionsValidator>();

        services.Configure<ForwardedHeadersOptions>(options =>
        {
            var proxy = configuration.GetSection(ReverseProxyOptions.SectionName).Get<ReverseProxyOptions>()
                ?? new ReverseProxyOptions();

            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            options.ForwardLimit = proxy.ForwardLimit;

            // The framework seeds both lists with loopback. Leaving that in place would trust a
            // forwarding header from anything sharing the host — a sidecar, another container on the
            // same network namespace, or a process a local attacker started. The trust set is
            // exactly what configuration names, so a loopback proxy has to be listed like any other.
            options.KnownProxies.Clear();
            options.KnownIPNetworks.Clear();

            foreach (var address in proxy.KnownProxies)
            {
                if (IPAddress.TryParse(address, out var parsed))
                {
                    options.KnownProxies.Add(parsed);
                }
            }

            foreach (var network in proxy.KnownNetworks)
            {
                if (IPNetwork.TryParse(network, out var parsed))
                {
                    options.KnownIPNetworks.Add(parsed);
                }
            }

            // X-Forwarded-Host is deliberately absent from ForwardedHeaders above: honouring it lets
            // a trusted proxy's client influence link generation and host-based routing, and
            // AllowedHosts is the control for that instead.
        });

        return services;
    }

    /// <summary>
    /// Must run before anything that reads the client address or the scheme — the rate limiter, CORS,
    /// authentication and request logging all do.
    /// </summary>
    public static WebApplication UseApiForwardedHeaders(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var proxy = app.Services.GetRequiredService<IOptions<ReverseProxyOptions>>().Value;

        if (!proxy.Enabled)
        {
            return app;
        }

        app.UseForwardedHeaders();
        return app;
    }
}

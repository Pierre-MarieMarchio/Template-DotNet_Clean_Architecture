using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

// Microsoft.AspNetCore.HttpOverrides also declares IPNetwork, deprecated in favour of this one.
using IPNetwork = System.Net.IPNetwork;

namespace AppTemplate.Api.Core.Common.Security;

/// <summary>
/// Translates <c>X-Forwarded-For</c> and <c>X-Forwarded-Proto</c> into
/// <see cref="HttpContext.Connection"/> and the request scheme, but only for peers named in
/// configuration. The rate limiter partitions on the remote address, so without this every caller
/// behind a proxy shares one window; with it misconfigured, every caller chooses its own.
/// </summary>
internal static class ForwardedHeadersExtensions
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

            // The framework seeds both lists with loopback, which would trust a forwarding header
            // from anything sharing the host. The trust set is exactly what configuration names.
            options.KnownProxies.Clear();
            options.KnownIPNetworks.Clear();

            foreach (string address in proxy.KnownProxies)
            {
                if (IPAddress.TryParse(address, out var parsed))
                {
                    options.KnownProxies.Add(parsed);
                }
            }

            foreach (string network in proxy.KnownNetworks)
            {
                if (IPNetwork.TryParse(network, out var parsed))
                {
                    options.KnownIPNetworks.Add(parsed);
                }
            }

            // X-Forwarded-Host is absent from ForwardedHeaders above: honouring it would let a
            // client influence link generation and host-based routing. AllowedHosts controls that.
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

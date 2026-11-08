using System.Net;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Options;

using IPNetwork = System.Net.IPNetwork;

namespace AppTemplate.Api.Common.Security;

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
        });

        return services;
    }

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

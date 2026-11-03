using AppTemplate.Presentation.Core.Common.Security;
using Microsoft.Extensions.Options;

namespace AppTemplate.PresentationAppPortal.Common.Security;

internal static class SecurityHeadersExtensions
{
    internal static IServiceCollection AddPortalSecurityHeaders(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        return services.AddSecurityHeaderOptions(configuration, PortalContentSecurityPolicy.Default);
    }

    internal static WebApplication UsePortalSecurityHeaders(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        string policy = app.Services.GetRequiredService<IOptions<SecurityHeaderOptions>>()
            .Value.ContentSecurityPolicy;

        app.Use((context, next) =>
        {
            context.Response.OnStarting(
                static state =>
                {
                    var (headers, contentSecurityPolicy) = ((IHeaderDictionary, string))state;

                    foreach ((string name, string value) in SecurityHeaderValues.Fixed)
                    {
                        headers[name] = value;
                    }

                    headers.Remove(SecurityHeaderValues.PoweredByHeader);
                    headers[SecurityHeaderValues.ContentSecurityPolicyHeader] = contentSecurityPolicy;

                    return Task.CompletedTask;
                },
                (context.Response.Headers, policy));

            return next(context);
        });

        return app;
    }
}

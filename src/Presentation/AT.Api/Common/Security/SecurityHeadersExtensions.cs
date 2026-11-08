using AppTemplate.Presentation.Core.Common.Security;
using Microsoft.Extensions.Options;

namespace AppTemplate.Api.Common.Security;

internal static class SecurityHeadersExtensions
{
    internal static IServiceCollection AddApiSecurityHeaders(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        return services.AddSecurityHeaderOptions(configuration);
    }

    internal static WebApplication UseApiSecurityHeaders(
        this WebApplication app,
        ResponseContentSecurityPolicy? policyOverride)
    {
        ArgumentNullException.ThrowIfNull(app);

        string apiPolicy = app.Services.GetRequiredService<IOptions<SecurityHeaderOptions>>()
            .Value.ContentSecurityPolicy;

        app.Use((context, next) =>
        {
            context.Response.OnStarting(
                static state =>
                {
                    var (httpContext, policy, over) =
                        ((HttpContext, string, ResponseContentSecurityPolicy?))state;

                    Write(httpContext, policy, over);

                    return Task.CompletedTask;
                },
                (context, apiPolicy, policyOverride));

            return next(context);
        });

        return app;
    }

    private static void Write(
        HttpContext httpContext,
        string apiPolicy,
        ResponseContentSecurityPolicy? policyOverride)
    {
        var headers = httpContext.Response.Headers;

        foreach ((string name, string value) in SecurityHeaderValues.Fixed)
        {
            headers[name] = value;
        }

        headers.Remove(SecurityHeaderValues.PoweredByHeader);

        headers[SecurityHeaderValues.ContentSecurityPolicyHeader] = policyOverride?.Invoke(httpContext) ?? apiPolicy;
    }
}

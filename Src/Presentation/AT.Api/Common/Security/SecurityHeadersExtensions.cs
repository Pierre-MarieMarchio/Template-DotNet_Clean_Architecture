using Microsoft.Extensions.Options;

namespace AppTemplate.Api.Common.Security;

internal static class SecurityHeadersExtensions
{
    private const string _referrerPolicyHeader = "Referrer-Policy";

    private const string _referrerPolicy = "no-referrer";

    private const string _frameOptions = "DENY";

    private const string _poweredByHeader = "X-Powered-By";

    internal static IServiceCollection AddApiSecurityHeaders(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<SecurityHeaderOptions>()
            .Bind(configuration.GetSection(SecurityHeaderOptions.SectionName))
            .ValidateOnStart();

        services.AddSingleton<IValidateOptions<SecurityHeaderOptions>, SecurityHeaderOptionsValidator>();

        return services;
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

        headers.XContentTypeOptions = "nosniff";
        headers[_referrerPolicyHeader] = _referrerPolicy;
        headers.XFrameOptions = _frameOptions;
        headers.Remove(_poweredByHeader);

        headers.ContentSecurityPolicy = policyOverride?.Invoke(httpContext) ?? apiPolicy;
    }
}

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace AppTemplate.Api.Core.Common.Security;

/// <summary>
/// The response headers that tell a browser what it may do with what this origin returns.
/// </summary>
/// <remarks>
/// Every header is written from a <see cref="HttpResponse.OnStarting(Func{object, Task}, object)"/>
/// callback rather than set as the middleware runs. That is required, not stylistic:
/// <c>UseExceptionHandler</c> calls <c>Response.Clear()</c> before it re-runs the pipeline, so a
/// header set on the way in would be dropped from exactly the responses — the 5xx ProblemDetails —
/// that most need it. An <c>OnStarting</c> registration survives that reset.
/// </remarks>
internal static class SecurityHeadersExtensions
{
    private const string _referrerPolicyHeader = "Referrer-Policy";

    /// <summary>
    /// Nothing at all, rather than <c>strict-origin-when-cross-origin</c>. This origin serves JSON
    /// whose paths are resource identifiers, and there is no first-party page of ours that needs a
    /// referrer, so leaking even the origin buys nothing.
    /// </summary>
    private const string _referrerPolicy = "no-referrer";

    /// <summary>
    /// Kept alongside <c>frame-ancestors</c> for agents predating CSP Level 2. A browser that
    /// understands both prefers the directive, so the two cannot disagree in practice.
    /// </summary>
    private const string _frameOptions = "DENY";

    /// <summary>Emitted by IIS and by some reverse proxies; it only ever tells an attacker something.</summary>
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

    /// <summary>
    /// Install early, so that a response produced by the exception handler, the rate limiter or a
    /// health endpoint is covered as well as one produced by a controller.
    /// </summary>
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

        // The override decides per response, and answers null for every path it does not govern.
        headers.ContentSecurityPolicy = policyOverride?.Invoke(httpContext) ?? apiPolicy;
    }
}

using Microsoft.Extensions.Options;
using Scalar.AspNetCore;

namespace AppTemplate.Api.Common.Security;

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
public static class SecurityHeadersPolicies
{
    /// <summary>Where the API-reference page and its own assets are served in Development.</summary>
    public const string ApiReferencePathPrefix = "/scalar";

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

    public static IServiceCollection AddApiSecurityHeaders(
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
    public static WebApplication UseApiSecurityHeaders(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        string apiPolicy = app.Services.GetRequiredService<IOptions<SecurityHeaderOptions>>()
            .Value.ContentSecurityPolicy;

        bool servesApiReference = app.Environment.IsDevelopment();

        app.Use((context, next) =>
        {
            context.Response.OnStarting(
                static state =>
                {
                    var (httpContext, policy, documentationIsServed) = ((HttpContext, string, bool))state;

                    Write(httpContext, policy, documentationIsServed);

                    return Task.CompletedTask;
                },
                (context, apiPolicy, servesApiReference));

            return next(context);
        });

        return app;
    }

    /// <summary>
    /// What the API-reference page needs, and nothing beyond it. Each directive answers something the
    /// shipped <c>Scalar.AspNetCore</c> bundle actually does:
    /// <list type="bullet">
    /// <item><c>script-src</c>: two same-origin files under the prefix, plus one inline
    /// <c>&lt;script type="module"&gt;</c> that the nonce covers.</item>
    /// <item><c>style-src 'unsafe-inline'</c>: the bundle mounts its stylesheet by creating a
    /// <c>&lt;style&gt;</c> element and assigning <c>textContent</c>, and its components carry
    /// <c>style</c> attributes. Neither can be nonced from here.</item>
    /// <item><c>img-src</c>: the served <c>favicon.svg</c>, <c>data:</c> images inlined in the
    /// bundle's CSS, and <c>blob:</c> previews of a response body.</item>
    /// <item><c>font-src</c>: the bundle's <c>@font-face</c> rules load Inter from
    /// <c>fonts.scalar.com</c>. Declaring the host is honest about what the page fetches; hiding it
    /// by turning the fonts off would be changing the page to fit the policy.</item>
    /// <item><c>connect-src 'self'</c>: it fetches the OpenAPI document, and "try it" posts to this
    /// same origin.</item>
    /// <item><c>worker-src</c>: it starts a module worker from an object URL.</item>
    /// </list>
    /// </summary>
    private static string ApiReferencePolicy(string? nonce)
    {
        string scriptSource = string.IsNullOrEmpty(nonce) ? "'self'" : $"'self' 'nonce-{nonce}'";

        return "default-src 'none'; " +
            $"script-src {scriptSource}; " +
            "style-src 'self' 'unsafe-inline'; " +
            "img-src 'self' data: blob:; " +
            "font-src https://fonts.scalar.com; " +
            "connect-src 'self'; " +
            "worker-src 'self' blob:; " +
            "frame-ancestors 'none'; " +
            "base-uri 'none'; " +
            "form-action 'none'";
    }

    private static void Write(HttpContext httpContext, string apiPolicy, bool documentationIsServed)
    {
        var headers = httpContext.Response.Headers;

        headers.XContentTypeOptions = "nosniff";
        headers[_referrerPolicyHeader] = _referrerPolicy;
        headers.XFrameOptions = _frameOptions;
        headers.Remove(_poweredByHeader);

        headers.ContentSecurityPolicy = documentationIsServed && IsApiReference(httpContext.Request.Path)
            ? ApiReferencePolicy(NonceOf(httpContext))
            : apiPolicy;
    }

    /// <summary>
    /// The nonce <c>WithNonce()</c> generated for this request. It is written while the endpoint runs,
    /// which is after this middleware but before the response starts — which is the whole reason the
    /// headers are written from <c>OnStarting</c>.
    /// </summary>
    private static string? NonceOf(HttpContext httpContext) =>
        httpContext.Items.TryGetValue(ScalarOptions.NonceHttpContextItemKey, out object? nonce)
            ? nonce as string
            : null;

    private static bool IsApiReference(PathString path) =>
        path.StartsWithSegments(ApiReferencePathPrefix, StringComparison.OrdinalIgnoreCase);
}

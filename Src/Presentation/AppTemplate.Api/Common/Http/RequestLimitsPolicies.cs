using AppTemplate.Api.Common.Errors;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Options;

namespace AppTemplate.Api.Common.Http;

/// <summary>
/// Caps how large a request body this API accepts, at two points that each catch what the other
/// cannot.
/// </summary>
public static class RequestLimitsPolicies
{
    public static IServiceCollection AddApiRequestLimits(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<RequestLimitsOptions>()
            .Bind(configuration.GetSection(RequestLimitsOptions.SectionName))
            .ValidateOnStart();

        services.AddSingleton<IValidateOptions<RequestLimitsOptions>, RequestLimitsOptionsValidator>();

        // Kestrel's own limit, sourced from the same option, as the backstop for a chunked request
        // that carries no Content-Length header — the one shape UseApiRequestLimits below has nothing
        // to compare against, because it reads Content-Length and nothing else.
        services.AddOptions<KestrelServerOptions>()
            .Configure<IOptions<RequestLimitsOptions>>(
                static (kestrelOptions, requestLimits) =>
                    kestrelOptions.Limits.MaxRequestBodySize = requestLimits.Value.MaxRequestBodyBytes);

        return services;
    }

    /// <summary>
    /// Install early, before anything reads the body. This exists alongside Kestrel's own limit —
    /// rather than instead of it — because the integration tests run on <c>TestServer</c>, where
    /// Kestrel's limits do not apply at all: a Kestrel-only limit would be untestable and therefore
    /// unverified. This middleware is what a test actually drives; Kestrel's limit is what a real
    /// deployment falls back on for a chunked request this middleware cannot see coming.
    /// </summary>
    public static WebApplication UseApiRequestLimits(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        long maxBytes = app.Services.GetRequiredService<IOptions<RequestLimitsOptions>>().Value.MaxRequestBodyBytes;

        app.Use(async (context, next) =>
        {
            if (context.Request.ContentLength is { } contentLength && contentLength > maxBytes)
            {
                await WriteTooLargeAsync(context);
                return;
            }

            await next(context);
        });

        return app;
    }

    private static async Task WriteTooLargeAsync(HttpContext httpContext)
    {
        const int status = StatusCodes.Status413PayloadTooLarge;

        var problem = new ProblemDetails
        {
            Status = status,
            Title = "Payload too large",
            Detail = "The request body exceeds the maximum size this API accepts.",
        };

        // Normalise derives the same code from the status that CodeFor always gave this response,
        // and adds the traceId and type every other producer carries too.
        ProblemDetailsDefaults.Normalise(problem, httpContext);

        httpContext.Response.StatusCode = status;
        await httpContext.Response.WriteAsJsonAsync(
            problem,
            options: null,
            contentType: "application/problem+json",
            httpContext.RequestAborted);
    }
}

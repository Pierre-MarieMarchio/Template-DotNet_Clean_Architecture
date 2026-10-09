using AppTemplate.Api.Common.Errors;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Options;

namespace AppTemplate.Api.Common.Hosting;

/// <summary>
/// Caps how large a request body this API accepts, at two points that each catch what the other
/// cannot.
/// </summary>
internal static class RequestLimitsExtensions
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

        app.Use((context, next) => LimitAsync(context, next, maxBytes));

        return app;
    }

    /// <summary>
    /// Answers 413 when the declared length exceeds the limit: <paramref name="maxBytes"/>, or the
    /// endpoint's own <see cref="RequestBodyLimitAttribute"/>, which is then also applied to the
    /// server's limit and to a multipart body for this request.
    /// </summary>
    internal static Task LimitAsync(HttpContext context, RequestDelegate next, long maxBytes)
    {
        long limit = maxBytes;

        // The endpoint is known here: the host routes before the first middleware it is given.
        if (context.GetEndpoint()?.Metadata.GetMetadata<RequestBodyLimitAttribute>() is { } raised)
        {
            limit = raised.MaxRequestBodyBytes(context.RequestServices);
            Raise(context, limit);
        }

        if (context.Request.ContentLength is { } contentLength && contentLength > limit)
        {
            return WriteTooLargeAsync(context);
        }

        return next(context);
    }

    private static void Raise(HttpContext context, long limit)
    {
        // Kestrel's limit, for this request only; absent on TestServer, read-only once reading began.
        if (context.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } serverLimit)
        {
            serverLimit.MaxRequestBodySize = limit;
        }

        context.Features.Set<IFormFeature>(
            new FormFeature(context.Request, new FormOptions { MultipartBodyLengthLimit = limit }));
    }

    // Written by hand rather than through IProblemDetailsService, which writes nothing when the
    // request's Accept header excludes JSON: a refusal has to carry its body whatever was asked for.
    internal static async Task WriteTooLargeAsync(HttpContext httpContext)
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
        ProblemDetailsNormaliser.Normalise(problem, httpContext);

        httpContext.Response.StatusCode = status;
        await httpContext.Response.WriteAsJsonAsync(
            problem,
            options: null,
            contentType: "application/problem+json",
            httpContext.RequestAborted);
    }
}

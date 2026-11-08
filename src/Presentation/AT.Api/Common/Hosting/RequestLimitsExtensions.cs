using AppTemplate.Api.Common.Errors;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Options;

namespace AppTemplate.Api.Common.Hosting;

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

        services.AddOptions<KestrelServerOptions>()
            .Configure<IOptions<RequestLimitsOptions>>(
                static (kestrelOptions, requestLimits) =>
                    kestrelOptions.Limits.MaxRequestBodySize = requestLimits.Value.MaxRequestBodyBytes);

        return services;
    }

    public static WebApplication UseApiRequestLimits(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        long maxBytes = app.Services.GetRequiredService<IOptions<RequestLimitsOptions>>().Value.MaxRequestBodyBytes;

        app.Use((context, next) => LimitAsync(context, next, maxBytes));

        return app;
    }

    internal static Task LimitAsync(HttpContext context, RequestDelegate next, long maxBytes)
    {
        long limit = maxBytes;

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
        if (context.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } serverLimit)
        {
            serverLimit.MaxRequestBodySize = limit;
        }

        context.Features.Set<IFormFeature>(
            new FormFeature(context.Request, new FormOptions { MultipartBodyLengthLimit = limit }));
    }

    internal static async Task WriteTooLargeAsync(HttpContext httpContext)
    {
        const int status = StatusCodes.Status413PayloadTooLarge;

        var problem = new ProblemDetails
        {
            Status = status,
            Title = "Payload too large",
            Detail = "The request body exceeds the maximum size this API accepts.",
        };

        ProblemDetailsNormaliser.Normalise(problem, httpContext);

        httpContext.Response.StatusCode = status;
        await httpContext.Response.WriteAsJsonAsync(
            problem,
            options: null,
            contentType: "application/problem+json",
            httpContext.RequestAborted);
    }
}

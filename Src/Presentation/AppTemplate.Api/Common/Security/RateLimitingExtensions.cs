using System.Globalization;
using System.Threading.RateLimiting;
using AppTemplate.Api.Common.Errors;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace AppTemplate.Api.Common.Security;

public static class RateLimitingExtensions
{
    //#if (localAuth)
    public const string Authentication = "authentication";
    //#endif

    internal static IServiceCollection AddApiRateLimiting(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<RateLimitingOptions>()
            .Bind(configuration.GetSection(RateLimitingOptions.SectionName))
            .ValidateOnStart();

        services.AddSingleton<IValidateOptions<RateLimitingOptions>, RateLimitingOptionsValidator>();

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = WriteRejectionAsync;
        });

        services.AddOptions<RateLimiterOptions>().Configure<IOptions<RateLimitingOptions>>(
            static (options, limits) =>
            {
                var configured = limits.Value;

                //#if (localAuth)
                options.AddPolicy(
                    Authentication,
                    FixedWindowPerAddress(configured.AuthenticationPermitLimit, configured.Window));
                //#endif
                options.GlobalLimiter = PartitionedRateLimiter.Create(
                    FixedWindowPerAddress(configured.GlobalPermitLimit, configured.Window));
            });

        return services;
    }

    internal static Func<HttpContext, RateLimitPartition<string>> FixedWindowPerAddress(int permitLimit, TimeSpan window)
    {
        var options = new FixedWindowRateLimiterOptions
        {
            PermitLimit = permitLimit,
            Window = window,

            QueueLimit = 0,
        };

        return httpContext => RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: RateLimiterPartitionKeys.ForAddress(httpContext),
            factory: _ => options);
    }

    private static async ValueTask WriteRejectionAsync(OnRejectedContext context, CancellationToken cancellationToken)
    {
        if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
        {
            context.HttpContext.Response.Headers.RetryAfter =
                ((int)retryAfter.TotalSeconds).ToString(CultureInfo.InvariantCulture);
        }

        var problem = new ProblemDetails
        {
            Status = StatusCodes.Status429TooManyRequests,
            Title = "Too many requests",
            Detail = "Rate limit exceeded. Retry later.",
        };

        problem.Extensions["code"] = "rateLimit.exceeded";
        ProblemDetailsNormaliser.Normalise(problem, context.HttpContext);

        await context.HttpContext.Response.WriteAsJsonAsync(
            problem,
            options: null,
            contentType: "application/problem+json",
            cancellationToken);
    }
}

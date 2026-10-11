using System.Globalization;
using System.Threading.RateLimiting;
using AppTemplate.Api.Common.Errors;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace AppTemplate.Api.Common.Security;

/// <summary>
/// Rate limiting: together with account lockout, this is what bounds online password guessing.
/// </summary>
/// <remarks>
/// Counters live in this process's memory, one fixed window per client address, so the limit a
/// caller actually meets is the configured number multiplied by the replica count;
/// <c>docs/CONFIGURATION.md</c> prints that arithmetic. A shared count would put a store on the path
/// of every request, in a repository that has already refused a distributed cache twice.
/// </remarks>
public static class RateLimitingExtensions
{
    //#if (localAuth)
    /// <summary>
    /// Applied to the authentication controller, on every action that handles a credential. It
    /// would be wrong to partition this one by user even if identity were available: it exists to
    /// slow down credential discovery on endpoints where, by construction, there is no identity yet.
    /// </summary>
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

        // A second configure pass, because the limits come from the validated options and
        // AddRateLimiter's own delegate has no service provider to read them from.
        services.AddOptions<RateLimiterOptions>().Configure<IOptions<RateLimitingOptions>>(
            static (options, limits) =>
            {
                var configured = limits.Value;

                // Behind a reverse proxy this needs ForwardedHeaders configured, or every request
                // appears to come from the proxy and shares one partition either way.
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
        // One options object for every partition rather than one each. Safe to share: nothing
        // mutates it after this line.
        var options = new FixedWindowRateLimiterOptions
        {
            PermitLimit = permitLimit,
            Window = window,

            // A caller over budget is refused immediately rather than parked on a request thread,
            // which sheds load instead of holding it.
            QueueLimit = 0,
        };

        return httpContext => RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: RateLimiterPartitionKeys.ForAddress(httpContext),
            factory: _ => options);
    }

    // Written by hand rather than through IProblemDetailsService, which writes nothing when the
    // request's Accept header excludes JSON: a refusal has to carry its body whatever was asked for.
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

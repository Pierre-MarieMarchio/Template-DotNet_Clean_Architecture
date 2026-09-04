using System.Globalization;
using System.Threading.RateLimiting;
using AppTemplate.Api.Common.Errors;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AppTemplate.Api.Common.Security;

/// <summary>
/// Rate limiting: together with account lockout, this is what bounds online password guessing.
/// </summary>
/// <remarks>
/// This file owns the two budgets and what a refused caller is told; it does not own where the count
/// is kept. That is <see cref="IRateLimitCounters"/>, and the split is the only thing standing
/// between this template and a rate limit that a deployment can make hold across replicas — see that
/// interface for what an implementation of it may and may not decide.
/// </remarks>
public static class RateLimitingExtensions
{
    /// <summary>Applied to the authentication controller.</summary>
    public const string Authentication = "authentication";

    /// <summary>Permits per window on <see cref="Authentication"/>. A test asserts against this.</summary>
    public const int AuthenticationPermitLimit = 10;

    /// <summary>Permits per window on the global limiter. A test asserts against this.</summary>
    public const int GlobalPermitLimit = 300;

    public static IServiceCollection AddApiRateLimiting(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton(RateLimiterWindow.Default);
        services.TryAddSingleton<IRateLimitCounters, InProcessRateLimitCounters>();

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            options.OnRejected = async (context, cancellationToken) =>
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
            };
        });

        // A dependency-injected configure pass, separate from the one above, because it is the only
        // way to read RateLimiterWindow and IRateLimitCounters here: AddRateLimiter's own delegate
        // has no service provider to resolve from.
        services.AddOptions<RateLimiterOptions>().Configure<RateLimiterWindow, IRateLimitCounters>(
            (options, window, counters) =>
            {
                // Behind a reverse proxy this needs ForwardedHeaders configured, or every request
                // appears to come from the proxy and shares one partition either way.
                options.AddPolicy(
                    Authentication,
                    counters.PartitionerFor(new RateLimitBudget(AuthenticationPermitLimit, window.Duration)));

                options.GlobalLimiter = PartitionedRateLimiter.Create(
                    counters.PartitionerFor(new RateLimitBudget(GlobalPermitLimit, window.Duration)));
            });

        return services;
    }
}

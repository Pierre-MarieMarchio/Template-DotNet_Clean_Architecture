using AppTemplate.Api.Common.Errors;
using Microsoft.AspNetCore.Http.Timeouts;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace AppTemplate.Api.Common.Hosting;

/// <summary>
/// Host shutdown and request-deadline behaviour: how long a graceful stop waits, and how long a
/// request is allowed to run before it is cut loose.
/// </summary>
public static class HostLifecycleExtensions
{
    /// <summary>
    /// Name to pass to <c>[RequestTimeout(HostLifecycleExtensions.LongRequestTimeoutPolicy)]</c> on a
    /// non-streaming endpoint whose normal work legitimately runs longer than
    /// <see cref="RequestTimeoutsOptions.Default"/>. Read the XML doc on
    /// <see cref="RequestTimeoutsOptions.Extended"/> before reaching for this on a stream.
    /// </summary>
    public const string LongRequestTimeoutPolicy = "long";

    public static IServiceCollection AddApiLifecycle(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<ShutdownOptions>()
            .Bind(configuration.GetSection(ShutdownOptions.SectionName))
            .ValidateOnStart();

        services.AddSingleton<IValidateOptions<ShutdownOptions>, ShutdownOptionsValidator>();

        // HostOptions.ShutdownTimeout is what the Generic Host waits for IHostedService.StopAsync —
        // and, transitively, for Kestrel to drain in-flight connections — once shutdown starts.
        // Same idiom as RequestLimitsExtensions configuring KestrelServerOptions from its own options.
        services.AddOptions<HostOptions>()
            .Configure<IOptions<ShutdownOptions>>(
                static (hostOptions, shutdown) => hostOptions.ShutdownTimeout = shutdown.Value.Timeout);

        services.AddOptions<RequestTimeoutsOptions>()
            .Bind(configuration.GetSection(RequestTimeoutsOptions.SectionName))
            .ValidateOnStart();

        services.AddSingleton<IValidateOptions<RequestTimeoutsOptions>, RequestTimeoutsOptionsValidator>();

        // AddRequestTimeouts builds its policies once, synchronously, at registration time — there
        // is no per-request IOptions access here — so the section is read eagerly, the same way
        // AddApiObservability reads TelemetryOptions before deciding what to wire. ValidateOnStart
        // above still fails the host on an out-of-range value; this is what actually acts on it.
        var requestTimeouts = configuration.GetSection(RequestTimeoutsOptions.SectionName).Get<RequestTimeoutsOptions>()
            ?? new RequestTimeoutsOptions();

        services.AddRequestTimeouts(options =>
        {
            options.DefaultPolicy = new RequestTimeoutPolicy
            {
                Timeout = requestTimeouts.Default,
                WriteTimeoutResponse = WriteTimeoutProblemAsync,
            };

            options.AddPolicy(LongRequestTimeoutPolicy, new RequestTimeoutPolicy
            {
                Timeout = requestTimeouts.Extended,
                WriteTimeoutResponse = WriteTimeoutProblemAsync,
            });
        });

        return services;
    }

    /// <summary>
    /// Endpoint metadata (<c>[RequestTimeout]</c>, <c>[DisableRequestTimeout]</c>) is already
    /// resolvable at this point since routing runs ahead of every <c>app.Use</c> call in this
    /// pipeline; placement here is about deadline scope, not metadata visibility — installed after
    /// the rate limiter so a request the limiter rejects never starts a clock that then has to be
    /// torn down, and before authentication so the deadline covers it too.
    /// </summary>
    public static WebApplication UseApiRequestTimeouts(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.UseRequestTimeouts();

        return app;
    }

    /// <summary>
    /// This is where a request timeout is answered. It runs only while the response has not started
    /// yet; once headers are gone there is no channel left to answer through, and a timeout can only
    /// end as a truncated response — which is the reason a stream must have its timeout
    /// <b>disabled</b> rather than lengthened. Title, detail and code match
    /// <c>GlobalExceptionHandler</c>'s timeout arm, so a client cannot tell which path answered.
    /// </summary>
    private static async Task WriteTimeoutProblemAsync(HttpContext httpContext)
    {
        var problem = new ProblemDetails
        {
            Status = httpContext.Response.StatusCode,
            Title = "Request timeout",
            Detail = "The server did not complete the request within its configured timeout.",
        };

        problem.Extensions["code"] = "request.timeout";
        ProblemDetailsNormaliser.Normalise(problem, httpContext);

        // httpContext.RequestAborted is the timeout's own cancellation token, already signalled —
        // that is why this method is running at all. Passing it to the write would fail instantly.
        await httpContext.Response.WriteAsJsonAsync(
            problem,
            options: null,
            contentType: "application/problem+json",
            CancellationToken.None);
    }
}

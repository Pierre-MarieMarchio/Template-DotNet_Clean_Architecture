using System.Reflection;
using AppTemplate.Api.Core.Common.Hosting;
using AppTemplate.Presentation.Core.Common.Observability;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace AppTemplate.Api.Core.Common.Observability;

/// <summary>
/// What an HTTP host can say about its own telemetry on top of
/// <see cref="AppTemplate.Presentation.Core.Common.Observability.ObservabilityExtensions"/>: the
/// ASP.NET Core instrumentation, the meters this transport publishes, and the one request log entry
/// that ties a trace to a caller's <c>traceId</c>.
/// </summary>
public static class ObservabilityExtensions
{
    /// <summary>
    /// The tag that carries <see cref="HttpContext.TraceIdentifier"/> onto the request span. Named
    /// after the framework property rather than an OpenTelemetry convention, because it is an
    /// ASP.NET Core value and no semantic convention describes it.
    /// </summary>
    private const string _traceIdentifierTagName = "aspnetcore.trace_identifier";

    /// <summary>
    /// Registers traces and metrics for an HTTP host.
    /// </summary>
    /// <param name="services">The container being built.</param>
    /// <param name="configuration">Where the telemetry options are bound from.</param>
    /// <param name="host">
    /// The host's own assembly, which is what <c>service.name</c> and <c>service.version</c> are
    /// read from. Passing this project's assembly instead would make every host announce itself
    /// under one name.
    /// </param>
    /// <param name="tracing">
    /// The host's own trace sources — a database instrumentation, an <c>ActivitySource</c> declared
    /// in one of its feature folders. A shared project cannot name either.
    /// </param>
    /// <param name="metrics">The host's own meters, for the same reason.</param>
    /// <returns>The same collection, for chaining.</returns>
    public static IServiceCollection AddCoreObservability(
        this IServiceCollection services,
        IConfiguration configuration,
        Assembly host,
        Action<TracerProviderBuilder>? tracing = null,
        Action<MeterProviderBuilder>? metrics = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(host);

        return services.AddObservability(
            configuration,
            host,
            tracer =>
            {
                tracer.AddAspNetCoreInstrumentation(instrumentation =>
                {
                    instrumentation.Filter = static context => !IsHealthProbe(context.Request.Path);

                    // The span is what a caller's traceId has to lead to, so it carries that value.
                    instrumentation.EnrichWithHttpRequest = static (activity, request) =>
                        activity.SetTag(_traceIdentifierTagName, request.HttpContext.TraceIdentifier);
                });

                tracing?.Invoke(tracer);
            },
            meter =>
            {
                meter.AddAspNetCoreInstrumentation()
                    // aspnetcore.rate_limiting.requests, tagged by policy and result (acquired /
                    // endpoint_limiter / global_limiter / request_canceled) — the rejection count the
                    // rate limiter itself never surfaces anywhere else. It is named here because the
                    // limiter is registered here: a mechanism and its one diagnostic travel together.
                    .AddMeter("Microsoft.AspNetCore.RateLimiting");

                metrics?.Invoke(meter);
            });
    }

    /// <summary>
    /// Install outside the exception handler, so the entry reports the status code the caller actually
    /// received rather than the one that was on its way to being replaced.
    /// </summary>
    internal static WebApplication UseApiRequestLogging(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.UseWhen(
            static context => !IsHealthProbe(context.Request.Path),
            branch => branch.UseMiddleware<RequestLoggingMiddleware>());

        return app;
    }

    private static bool IsHealthProbe(PathString path) =>
        path.StartsWithSegments(HealthEndpoints.PathPrefix, StringComparison.OrdinalIgnoreCase);
}

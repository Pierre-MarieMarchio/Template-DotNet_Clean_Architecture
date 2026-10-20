using AppTemplate.Api.Common.Hosting;
using AppTemplate.Presentation.Core.Common.Observability;
using Npgsql;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace AppTemplate.Api.Common.Observability;

/// <summary>
/// What this host can say about its own telemetry on top of
/// <see cref="AppTemplate.Presentation.Core.Common.Observability.ObservabilityExtensions"/>: the
/// ASP.NET Core instrumentation, the meters this transport publishes, which database it talks to,
/// and the one request log entry that ties a trace to a caller's <c>traceId</c>.
/// </summary>
/// <remarks>
/// <c>service.name</c> is read from this assembly, so the host reports under its own name. The
/// database instrumentation is registered here rather than in a module because its package carries
/// the PostgreSQL driver.
/// </remarks>
public static class ObservabilityExtensions
{
    /// <summary>
    /// The tag that carries <see cref="HttpContext.TraceIdentifier"/> onto the request span. Named
    /// after the framework property rather than an OpenTelemetry convention, because it is an
    /// ASP.NET Core value and no semantic convention describes it.
    /// </summary>
    private const string _traceIdentifierTagName = "aspnetcore.trace_identifier";

    /// <summary>Registers traces and metrics for this host.</summary>
    /// <param name="services">The container being built.</param>
    /// <param name="configuration">Where the telemetry options are bound from.</param>
    /// <returns>The same collection, for chaining.</returns>
    public static IServiceCollection AddApiObservability(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        return services.AddObservability(
            configuration,
            typeof(ObservabilityExtensions).Assembly,
            tracer =>
            {
                tracer.AddAspNetCoreInstrumentation(instrumentation =>
                {
                    instrumentation.Filter = static context => !IsHealthProbe(context.Request.Path);

                    // The span is what a caller's traceId has to lead to, so it carries that value.
                    instrumentation.EnrichWithHttpRequest = static (activity, request) =>
                        activity.SetTag(_traceIdentifierTagName, request.HttpContext.TraceIdentifier);
                });

                // Npgsql's own ActivitySource, which spans the command at the ADO.NET level — where the
                // SQL and its duration are. It needs no EF Core instrumentation on top.
                tracer.AddNpgsql();
            },
            meter =>
            {
                meter.AddAspNetCoreInstrumentation()
                    // aspnetcore.rate_limiting.requests, tagged by policy and result: the rejection count the limiter surfaces
                    // nowhere else. Named here because the limiter is registered here.
                    .AddMeter("Microsoft.AspNetCore.RateLimiting");

                // db.client.connection.count vs .max answers pool saturation (Database:MaxPoolSize), and ...pending_requests
                // counts callers already queued for a connection: both invisible from the span, which starts once one is
                // handed out.
                meter.AddMeter("Npgsql");
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

using System.Reflection;
using Microsoft.Extensions.Options;
using Npgsql;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace AppTemplate.Api.Common.Observability;

/// <summary>
/// Traces, metrics and the one request log entry that ties them to a caller's <c>traceId</c>.
/// </summary>
public static class ObservabilityExtensions
{
    /// <summary>
    /// The tag that carries <see cref="HttpContext.TraceIdentifier"/> onto the request span. Named
    /// after the framework property rather than an OpenTelemetry convention, because it is an
    /// ASP.NET Core value and no semantic convention describes it.
    /// </summary>
    public const string TraceIdentifierTagName = "aspnetcore.trace_identifier";

    /// <summary>Health probes: frequent, uninteresting, and they would dominate every signal.</summary>
    private const string _healthPathPrefix = "/health";

    public static IServiceCollection AddApiObservability(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<TelemetryOptions>()
            .Bind(configuration.GetSection(TelemetryOptions.SectionName))
            .ValidateOnStart();

        services.AddSingleton<IValidateOptions<TelemetryOptions>, TelemetryOptionsValidator>();

        var telemetry = configuration.GetSection(TelemetryOptions.SectionName).Get<TelemetryOptions>()
            ?? new TelemetryOptions();

        // Nothing is registered when nothing is configured: no instrumentation, no exporter, no
        // background flush. That is what lets the app run with no collector anywhere near it, rather
        // than running an exporter that retries against a socket that will never answer.
        if (!telemetry.Enabled)
        {
            return services;
        }

        var endpoint = new Uri(telemetry.OtlpEndpoint!, UriKind.Absolute);

        services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService(
                serviceName: telemetry.ServiceName ?? ServiceName,
                serviceVersion: ServiceVersion,
                serviceInstanceId: Environment.MachineName))
            .WithTracing(tracing => tracing
                // TraceIdRatioBasedSampler decides once, at the root, so a trace is never kept for
                // some of its spans and dropped for the rest. A ratio of 1 (the default) behaves
                // exactly like the SDK's own AlwaysOn sampler.
                .SetSampler(new ParentBasedSampler(new TraceIdRatioBasedSampler(telemetry.TracesSamplingRatio)))
                .AddAspNetCoreInstrumentation(instrumentation =>
                {
                    instrumentation.Filter = static context => !IsHealthProbe(context.Request.Path);

                    // The span is what a caller's traceId has to lead to, so it carries that value.
                    instrumentation.EnrichWithHttpRequest = static (activity, request) =>
                        activity.SetTag(TraceIdentifierTagName, request.HttpContext.TraceIdentifier);
                })
                .AddHttpClientInstrumentation()
                // Npgsql's own ActivitySource, which spans the command at the ADO.NET level — where
                // the SQL and its duration are. It needs no EF Core instrumentation on top.
                .AddNpgsql()
                .AddOtlpExporter(exporter =>
                {
                    exporter.Endpoint = endpoint;
                    exporter.Protocol = telemetry.OtlpProtocol;
                }))
            .WithMetrics(metrics => metrics
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                // Built into the runtime and Npgsql themselves — no extra package. GC, thread-pool
                // and process counters are the only way to tell "the box is under pressure" apart
                // from "the box is slow for some other reason".
                .AddMeter("System.Runtime")
                // db.client.connection.count vs .max is the pool-saturation question Database:MaxPoolSize
                // exists to answer; db.client.connection.npgsql.pending_requests is callers already
                // queued for a connection — both invisible today because the Npgsql span in the trace
                // above only starts once a connection has been handed out.
                .AddMeter("Npgsql")
                // aspnetcore.rate_limiting.requests, tagged by policy and result (acquired /
                // endpoint_limiter / global_limiter / request_canceled) — the rejection count the
                // rate limiter itself never surfaces anywhere else.
                .AddMeter("Microsoft.AspNetCore.RateLimiting")
                .AddOtlpExporter(exporter =>
                {
                    exporter.Endpoint = endpoint;
                    exporter.Protocol = telemetry.OtlpProtocol;
                }));

        return services;
    }

    /// <summary>
    /// Install outside the exception handler, so the entry reports the status code the caller actually
    /// received rather than the one that was on its way to being replaced.
    /// </summary>
    public static WebApplication UseApiRequestLogging(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.UseWhen(
            static context => !IsHealthProbe(context.Request.Path),
            branch => branch.UseMiddleware<RequestLoggingMiddleware>());

        return app;
    }

    /// <summary>Read from the assembly, so a rename or a version bump cannot leave a stale literal.</summary>
    private static string ServiceName { get; } =
        typeof(ObservabilityExtensions).Assembly.GetName().Name ?? "AppTemplate.Api";

    private static string? ServiceVersion { get; } = ReadInformationalVersion();

    private static string? ReadInformationalVersion()
    {
        var assembly = typeof(ObservabilityExtensions).Assembly;

        string? informational = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

        if (string.IsNullOrWhiteSpace(informational))
        {
            return assembly.GetName().Version?.ToString();
        }

        // A deterministic build appends '+<commit>', which is build provenance rather than a version.
        int metadata = informational.IndexOf('+', StringComparison.Ordinal);

        return metadata < 0 ? informational : informational[..metadata];
    }

    private static bool IsHealthProbe(PathString path) =>
        path.StartsWithSegments(_healthPathPrefix, StringComparison.OrdinalIgnoreCase);
}

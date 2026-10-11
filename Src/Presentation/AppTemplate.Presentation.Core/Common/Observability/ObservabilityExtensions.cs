using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace AppTemplate.Presentation.Core.Common.Observability;

/// <summary>Traces and metrics over OTLP, for any host: the options, the resource, the sampler, the
/// outbound-HTTP instrumentation and the exporter. Everything that names the host is the caller's: a
/// host's own <c>ActivitySource</c> and <c>Meter</c> names live in its feature folders — a shared
/// extension listing them would have to reference the host that references it — and <c>host</c> is
/// an assembly rather than a string so name and version are read from it, not a stale literal. No
/// ASP.NET Core instrumentation here and none reachable — this project carries no framework
/// reference, so an HTTP host adds its own through <c>tracing</c> and <c>metrics</c>, which is also
/// where database instrumentation goes, keeping <c>AddNpgsql()</c> out of every presentation
/// host.</summary>
public static class ObservabilityExtensions
{
    /// <summary>Binds and validates <see cref="TelemetryOptions"/>, and — only when enabled —
    /// registers the tracer and meter providers. Nothing at all is registered when nothing is
    /// configured, so a deployment can run with no collector near it rather than an exporter retrying
    /// against a socket that will never answer.</summary>
    /// <param name="services">The container being built.</param>
    /// <param name="configuration">Read for the <see cref="TelemetryOptions.SectionName"/> section.</param>
    /// <param name="host">The host's own assembly: its simple name is the <c>service.name</c> unless
    /// <see cref="TelemetryOptions.ServiceName"/> overrides it, and its informational version the
    /// <c>service.version</c>.</param>
    /// <param name="tracing">The host's own spans — one <c>AddSource</c> per <c>ActivitySource</c> it
    /// declares — plus any instrumentation that would drag a transport or driver into this project.</param>
    /// <param name="metrics">The host's own meters, on the same terms.</param>
    /// <returns>The same collection, for chaining.</returns>
    public static IServiceCollection AddObservability(
        this IServiceCollection services,
        IConfiguration configuration,
        Assembly host,
        Action<TracerProviderBuilder>? tracing = null,
        Action<MeterProviderBuilder>? metrics = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(host);

        services.AddOptions<TelemetryOptions>()
            .Bind(configuration.GetSection(TelemetryOptions.SectionName))
            .ValidateOnStart();

        services.AddSingleton<IValidateOptions<TelemetryOptions>, TelemetryOptionsValidator>();

        var telemetry = configuration.GetSection(TelemetryOptions.SectionName).Get<TelemetryOptions>()
            ?? new TelemetryOptions();

        if (!telemetry.Enabled)
        {
            return services;
        }

        var endpoint = new Uri(telemetry.OtlpEndpoint!, UriKind.Absolute);

        services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService(
                serviceName: telemetry.ServiceName ?? SimpleNameOf(host),
                serviceVersion: VersionOf(host),
                serviceInstanceId: Environment.MachineName))
            .WithTracing(builder =>
            {
                // TraceIdRatioBasedSampler decides once, at the root, so a trace is never kept for
                // some of its spans and dropped for the rest. A ratio of 1 (the default) behaves
                // exactly like the SDK's own AlwaysOn sampler.
                builder.SetSampler(
                    new ParentBasedSampler(new TraceIdRatioBasedSampler(telemetry.TracesSamplingRatio)));

                // A call made without a span is a call nobody can see failed.
                builder.AddHttpClientInstrumentation();

                // Before the exporter, so a host can still set sampling or add a processor.
                tracing?.Invoke(builder);

                builder.AddOtlpExporter(exporter =>
                {
                    exporter.Endpoint = endpoint;
                    exporter.Protocol = telemetry.OtlpProtocol;
                });
            })
            .WithMetrics(builder =>
            {
                builder.AddHttpClientInstrumentation();

                // Built into the runtime, no extra package. GC, thread-pool and process counters are
                // what tell "under pressure" apart from "slow for another reason".
                builder.AddMeter("System.Runtime");

                metrics?.Invoke(builder);

                builder.AddOtlpExporter(exporter =>
                {
                    exporter.Endpoint = endpoint;
                    exporter.Protocol = telemetry.OtlpProtocol;
                });
            });

        return services;
    }

    private static string SimpleNameOf(Assembly host) =>
        host.GetName().Name
        ?? throw new ArgumentException("The host assembly has no simple name.", nameof(host));

    private static string? VersionOf(Assembly host)
    {
        string? informational = host
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

        if (string.IsNullOrWhiteSpace(informational))
        {
            return host.GetName().Version?.ToString();
        }

        // A deterministic build appends '+<commit>', which is build provenance rather than a version.
        int metadata = informational.IndexOf('+', StringComparison.Ordinal);

        return metadata < 0 ? informational : informational[..metadata];
    }
}

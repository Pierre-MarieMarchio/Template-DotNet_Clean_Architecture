using System.Reflection;
using AppTemplate.Worker.Common.Maintenance;
using Microsoft.Extensions.Options;
using Npgsql;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace AppTemplate.Worker.Common.Observability;

/// <summary>
/// Traces and metrics for the worker host. AppTemplate.Api's own <c>ObservabilityPolicies</c>
/// cannot be reused here: it lives in the Api project, which this Worker deliberately does not
/// reference — see AppTemplate.Worker.csproj. A third project shared by both hosts would be the
/// cleaner home for this, but splitting it out touches the solution file and both hosts' project
/// files, which is a bigger change than adding telemetry to one of them; this class is the
/// self-contained alternative, kept deliberately small (no ASP.NET Core instrumentation, no HTTP
/// client instrumentation — this host answers no requests and calls no HTTP API).
/// </summary>
public static class WorkerObservability
{
    public static IServiceCollection AddWorkerObservability(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<WorkerTelemetryOptions>()
            .Bind(configuration.GetSection(WorkerTelemetryOptions.SectionName))
            .ValidateOnStart();

        services.AddSingleton<IValidateOptions<WorkerTelemetryOptions>, WorkerTelemetryOptionsValidator>();

        var telemetry = configuration.GetSection(WorkerTelemetryOptions.SectionName).Get<WorkerTelemetryOptions>()
            ?? new WorkerTelemetryOptions();

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
                // The maintenance loop's own span per task, plus the Npgsql span for the delete it
                // issues — the same pairing AppTemplate.Api gets for a request and its query.
                .AddSource(MaintenanceDiagnostics.Name)
                .AddNpgsql()
                .AddOtlpExporter(exporter =>
                {
                    exporter.Endpoint = endpoint;
                    exporter.Protocol = telemetry.OtlpProtocol;
                }))
            .WithMetrics(metrics => metrics
                .AddMeter(MaintenanceDiagnostics.Name)
                // Built into the runtime and Npgsql — no extra package, same as the Api host.
                .AddMeter("System.Runtime")
                .AddMeter("Npgsql")
                .AddOtlpExporter(exporter =>
                {
                    exporter.Endpoint = endpoint;
                    exporter.Protocol = telemetry.OtlpProtocol;
                }));

        return services;
    }

    private static string ServiceName { get; } =
        typeof(WorkerObservability).Assembly.GetName().Name ?? "AppTemplate.Worker";

    private static string? ServiceVersion { get; } = ReadInformationalVersion();

    private static string? ReadInformationalVersion()
    {
        var assembly = typeof(WorkerObservability).Assembly;

        string? informational = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

        if (string.IsNullOrWhiteSpace(informational))
        {
            return assembly.GetName().Version?.ToString();
        }

        int metadata = informational.IndexOf('+', StringComparison.Ordinal);

        return metadata < 0 ? informational : informational[..metadata];
    }
}

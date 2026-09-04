using System.Reflection;
using AppTemplate.Worker.Features.Maintenance;
using Microsoft.Extensions.Options;
using Npgsql;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace AppTemplate.Worker.Common.Observability;

/// <summary>
/// Traces and metrics for the worker host. AppTemplate.Api's own <c>ObservabilityExtensions</c>
/// cannot be reused here: it lives in the Api project, which this Worker deliberately does not
/// reference — see AppTemplate.Worker.csproj. A third project shared by both hosts would be the
/// cleaner home for this, but splitting it out touches the solution file and both hosts' project
/// files, which is a bigger change than adding telemetry to one of them; this class is the
/// self-contained alternative, kept deliberately small: no ASP.NET Core instrumentation, because
/// this host answers no request. Outbound HTTP <em>is</em> instrumented, matching the resilience
/// policy in <c>Common/Outbound/</c> — the modules this host composes are the ones that call
/// outwards, and a call this host makes without a span is a call nobody can see failed.
/// </summary>
public static class WorkerObservabilityExtensions
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
                .AddHttpClientInstrumentation()
                .AddOtlpExporter(exporter =>
                {
                    exporter.Endpoint = endpoint;
                    exporter.Protocol = telemetry.OtlpProtocol;
                }))
            .WithMetrics(metrics => metrics
                .AddMeter(MaintenanceDiagnostics.Name)
                .AddHttpClientInstrumentation()
                // "AppTemplate.Reminders": AppTemplate.Infrastructure.Persistence.Features
                // .Reminders.Observability.ReminderDiagnostics's own missed-cancellation counter. A
                // literal rather than a shared constant because that class is internal to a
                // different project — see its own doc for why — the same way "Npgsql" below names
                // a meter this host does not own either.
                .AddMeter("AppTemplate.Reminders")
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
        typeof(WorkerObservabilityExtensions).Assembly.GetName().Name ?? "AppTemplate.Worker";

    private static string? ServiceVersion { get; } = ReadInformationalVersion();

    private static string? ReadInformationalVersion()
    {
        var assembly = typeof(WorkerObservabilityExtensions).Assembly;

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

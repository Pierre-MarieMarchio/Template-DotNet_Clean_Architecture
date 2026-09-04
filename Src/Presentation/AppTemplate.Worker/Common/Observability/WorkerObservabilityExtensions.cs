using System.Reflection;
using AppTemplate.Worker.Features.Files;
using AppTemplate.Worker.Features.Maintenance;
using AppTemplate.Worker.Features.Reminders;
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
                // TraceIdRatioBasedSampler decides once, at the root, so a trace is never kept for
                // some of its spans and dropped for the rest. A ratio of 1 (the default) behaves
                // exactly like the SDK's own AlwaysOn sampler. Same construction as the Api host's:
                // both bind the same "OpenTelemetry" section, so a ratio set for a deployment has to
                // mean the same thing in both of its processes.
                .SetSampler(new ParentBasedSampler(new TraceIdRatioBasedSampler(telemetry.TracesSamplingRatio)))
                // Each loop's own span per task, plus the Npgsql span for the statements it issues —
                // the same pairing AppTemplate.Api gets for a request and its query. One AddSource
                // per host-owned ActivitySource, and
                // ObservabilityRegistrationTests.EveryDiagnosticsNameAHostDeclares_IsRegisteredByThatHost
                // fails the build for one that is missing.
                .AddSource(FileInstruments.Name)
                .AddSource(MaintenanceInstruments.Name)
                .AddSource(ReminderInstruments.Name)
                .AddNpgsql()
                .AddHttpClientInstrumentation()
                .AddOtlpExporter(exporter =>
                {
                    exporter.Endpoint = endpoint;
                    exporter.Protocol = telemetry.OtlpProtocol;
                }))
            .WithMetrics(metrics => metrics
                // The three loops' iteration counters and volume counters. All three are the
                // heartbeat an alert watches, and a meter this host declares but does not name here
                // is measured and thrown away at no lower cost than working — which is why
                // ObservabilityRegistrationTests fails the build for one that is missing rather
                // than leaving it to review.
                .AddMeter(FileInstruments.Name)
                .AddMeter(MaintenanceInstruments.Name)
                .AddMeter(ReminderInstruments.Name)
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

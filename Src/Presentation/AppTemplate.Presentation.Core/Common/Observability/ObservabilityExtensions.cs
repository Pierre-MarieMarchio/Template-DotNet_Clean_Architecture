using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace AppTemplate.Presentation.Core.Common.Observability;

public static class ObservabilityExtensions
{
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
                builder.SetSampler(
                    new ParentBasedSampler(new TraceIdRatioBasedSampler(telemetry.TracesSamplingRatio)));

                builder.AddHttpClientInstrumentation();

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

        int metadata = informational.IndexOf('+', StringComparison.Ordinal);

        return metadata < 0 ? informational : informational[..metadata];
    }
}

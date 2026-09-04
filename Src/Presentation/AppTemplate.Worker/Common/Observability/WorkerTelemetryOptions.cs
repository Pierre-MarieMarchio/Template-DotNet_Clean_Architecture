using Microsoft.Extensions.Options;
using OpenTelemetry.Exporter;

namespace AppTemplate.Worker.Common.Observability;

/// <summary>
/// Where this host's traces and metrics go, if anywhere. Deliberately the same shape as
/// AppTemplate.Api's own <c>TelemetryOptions</c> — same section name, same three knobs — so one
/// collector configuration covers both hosts. Not shared code: AppTemplate.Api's version lives in
/// the Api project, which this Worker does not and should not reference.
/// </summary>
public sealed class WorkerTelemetryOptions
{
    public const string SectionName = "OpenTelemetry";

    /// <summary>Off by default, for the same reason as the Api host: no collector, no exporter.</summary>
    public bool Enabled { get; set; }

    /// <summary>The collector's OTLP receiver, e.g. <c>http://localhost:4317</c>. Required when enabled.</summary>
    public string? OtlpEndpoint { get; set; }

    /// <summary><c>Grpc</c> (port 4317) or <c>HttpProtobuf</c> (port 4318).</summary>
    public OtlpExportProtocol OtlpProtocol { get; set; } = OtlpExportProtocol.Grpc;

    /// <summary>
    /// Overrides the <c>service.name</c> resource attribute, which otherwise comes from this
    /// assembly (<c>AppTemplate.Worker</c>, distinct from the Api host's own default). Set it when
    /// several deployments of this template share one collector.
    /// </summary>
    public string? ServiceName { get; set; }
}

internal sealed class WorkerTelemetryOptionsValidator : IValidateOptions<WorkerTelemetryOptions>
{
    public ValidateOptionsResult Validate(string? name, WorkerTelemetryOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (!options.Enabled)
        {
            return ValidateOptionsResult.Success;
        }

        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(options.OtlpEndpoint))
        {
            failures.Add(
                $"'{WorkerTelemetryOptions.SectionName}:{nameof(WorkerTelemetryOptions.OtlpEndpoint)}' is " +
                $"required when '{WorkerTelemetryOptions.SectionName}:{nameof(WorkerTelemetryOptions.Enabled)}' " +
                "is true.");
        }
        else if (!Uri.TryCreate(options.OtlpEndpoint, UriKind.Absolute, out var endpoint)
            || (endpoint.Scheme != Uri.UriSchemeHttp && endpoint.Scheme != Uri.UriSchemeHttps))
        {
            failures.Add(
                $"'{WorkerTelemetryOptions.SectionName}:{nameof(WorkerTelemetryOptions.OtlpEndpoint)}' must be " +
                $"an absolute http or https URL, such as 'http://localhost:4317'. It is " +
                $"'{options.OtlpEndpoint}'.");
        }

        if (!Enum.IsDefined(options.OtlpProtocol))
        {
            failures.Add(
                $"'{WorkerTelemetryOptions.SectionName}:{nameof(WorkerTelemetryOptions.OtlpProtocol)}' must be " +
                "'Grpc' or 'HttpProtobuf'.");
        }

        if (options.ServiceName is not null && string.IsNullOrWhiteSpace(options.ServiceName))
        {
            failures.Add(
                $"'{WorkerTelemetryOptions.SectionName}:{nameof(WorkerTelemetryOptions.ServiceName)}' is " +
                "present but blank. Remove the key to fall back to the assembly name.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}

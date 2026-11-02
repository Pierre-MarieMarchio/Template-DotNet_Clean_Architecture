using Microsoft.Extensions.Options;
using OpenTelemetry.Exporter;

namespace AppTemplate.Presentation.Core.Common.Observability;

public sealed class TelemetryOptions
{
    public const string SectionName = "OpenTelemetry";

    public bool Enabled { get; set; }

    public string? OtlpEndpoint { get; set; }

    public OtlpExportProtocol OtlpProtocol { get; set; } = OtlpExportProtocol.Grpc;

    public string? ServiceName { get; set; }

    public double TracesSamplingRatio { get; set; } = 1.0;
}

internal sealed class TelemetryOptionsValidator : IValidateOptions<TelemetryOptions>
{
    public ValidateOptionsResult Validate(string? name, TelemetryOptions options)
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
                $"'{TelemetryOptions.SectionName}:{nameof(TelemetryOptions.OtlpEndpoint)}' is required " +
                $"when '{TelemetryOptions.SectionName}:{nameof(TelemetryOptions.Enabled)}' is true.");
        }
        else if (!Uri.TryCreate(options.OtlpEndpoint, UriKind.Absolute, out var endpoint)
            || (endpoint.Scheme != Uri.UriSchemeHttp && endpoint.Scheme != Uri.UriSchemeHttps))
        {
            failures.Add(
                $"'{TelemetryOptions.SectionName}:{nameof(TelemetryOptions.OtlpEndpoint)}' must be an " +
                $"absolute http or https URL, such as 'http://localhost:4317'. It is " +
                $"'{options.OtlpEndpoint}'.");
        }

        if (!Enum.IsDefined(options.OtlpProtocol))
        {
            failures.Add(
                $"'{TelemetryOptions.SectionName}:{nameof(TelemetryOptions.OtlpProtocol)}' must be " +
                "'Grpc' or 'HttpProtobuf'.");
        }

        if (options.ServiceName is not null && string.IsNullOrWhiteSpace(options.ServiceName))
        {
            failures.Add(
                $"'{TelemetryOptions.SectionName}:{nameof(TelemetryOptions.ServiceName)}' is present but " +
                "blank. Remove the key to fall back to the assembly name.");
        }

        if (options.TracesSamplingRatio is not (> 0 and <= 1))
        {
            failures.Add(
                $"'{TelemetryOptions.SectionName}:{nameof(TelemetryOptions.TracesSamplingRatio)}' must be " +
                $"greater than 0 and at most 1. It is '{options.TracesSamplingRatio}'.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}

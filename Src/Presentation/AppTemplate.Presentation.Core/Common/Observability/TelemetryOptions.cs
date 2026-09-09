using Microsoft.Extensions.Options;
using OpenTelemetry.Exporter;

namespace AppTemplate.Presentation.Core.Common.Observability;

/// <summary>
/// Where traces and metrics go, if anywhere. One class for every host, binding one section, so a
/// sampling ratio set for a deployment means the same thing in each of its processes.
/// <para>
/// Public because it is bound from configuration and its section name is part of the template's
/// contract with whoever deploys it.
/// </para>
/// </summary>
public sealed class TelemetryOptions
{
    /// <summary>The configuration section this binds, and part of the deployment contract.</summary>
    public const string SectionName = "OpenTelemetry";

    /// <summary>
    /// Off by default. An unconfigured deployment installs no instrumentation and no exporter at all,
    /// so there is nothing to reconnect to a collector that does not exist.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// The collector's OTLP receiver, e.g. <c>http://localhost:4317</c>. Required when
    /// <see cref="Enabled"/> is set, because the exporter's own fallback is an unannounced
    /// <c>localhost:4317</c> — a deployment would then export into nothing and be told nothing.
    /// </summary>
    public string? OtlpEndpoint { get; set; }

    /// <summary>
    /// <c>Grpc</c> (port 4317) or <c>HttpProtobuf</c> (port 4318). Collectors differ, and an
    /// endpoint on the wrong protocol fails silently in the exporter's own diagnostics.
    /// </summary>
    public OtlpExportProtocol OtlpProtocol { get; set; } = OtlpExportProtocol.Grpc;

    /// <summary>
    /// Overrides the <c>service.name</c> resource attribute, which otherwise comes from the host
    /// assembly the caller names. Set it when several deployments of this template share one
    /// collector.
    /// <para>
    /// It cannot default to the assembly this class lives in: every host would then announce itself
    /// under the same name, and two processes reporting as one is worse than either reporting
    /// nothing. Which is why the registration takes the host's own assembly rather than reading its
    /// own.
    /// </para>
    /// </summary>
    public string? ServiceName { get; set; }

    /// <summary>
    /// Fraction of traces kept, applied at the root span so a whole trace is kept or dropped
    /// together. <c>1.0</c> (the default) keeps every trace — right for a first deployment, wrong
    /// once ingestion is sustained enough that a trace and an Npgsql span per command are exported
    /// for every single request. Lower once volume, not curiosity, calls for it.
    /// </summary>
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

        // Written as the range that passes, then negated: with NaN every comparison is false, so a
        // check written as "is <= 0 or > 1" would silently let NaN through.
        if (options.TracesSamplingRatio is not (> 0 and <= 1))
        {
            failures.Add(
                $"'{TelemetryOptions.SectionName}:{nameof(TelemetryOptions.TracesSamplingRatio)}' must be " +
                $"greater than 0 and at most 1. It is '{options.TracesSamplingRatio}'.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}

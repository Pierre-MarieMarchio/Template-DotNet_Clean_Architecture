using Microsoft.Extensions.Options;

namespace AppTemplate.Api.Common.Http;

/// <summary>
/// How large a request body this API accepts before Kestrel finishes reading it.
/// <para>
/// Public because it is bound from configuration and its section name is part of the template's
/// contract with whoever deploys it.
/// </para>
/// </summary>
public sealed class RequestLimitsOptions
{
    public const string SectionName = "RequestLimits";

    /// <summary>
    /// 64 KiB by default. Kestrel's own default is 30 MB, which is a free denial-of-service against a
    /// JSON API whose largest legitimate body is a few kilobytes; this is generous headroom over that
    /// without being large enough to let one request pin significant memory.
    /// </summary>
    public long MaxRequestBodyBytes { get; set; } = 65536;
}

internal sealed class RequestLimitsOptionsValidator : IValidateOptions<RequestLimitsOptions>
{
    /// <summary>Below this, even a legitimate request would be refused.</summary>
    private const long _minBytes = 1024;

    /// <summary>Above this, the setting is not a limit: it is Kestrel's own unconfigured default.</summary>
    private const long _maxBytes = 30 * 1024 * 1024;

    public ValidateOptionsResult Validate(string? name, RequestLimitsOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (options.MaxRequestBodyBytes < _minBytes || options.MaxRequestBodyBytes > _maxBytes)
        {
            return ValidateOptionsResult.Fail(
                $"'{RequestLimitsOptions.SectionName}:{nameof(RequestLimitsOptions.MaxRequestBodyBytes)}' " +
                $"is '{options.MaxRequestBodyBytes}', which must be between {_minBytes} and {_maxBytes} bytes " +
                "inclusive.");
        }

        return ValidateOptionsResult.Success;
    }
}

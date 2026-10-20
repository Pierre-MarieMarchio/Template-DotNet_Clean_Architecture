using Microsoft.Extensions.Options;

namespace AppTemplate.Api.Common.Hosting;

public sealed class RequestLimitsOptions
{
    public const string SectionName = "RequestLimits";

    public long MaxRequestBodyBytes { get; set; } = 65536;
}

internal sealed class RequestLimitsOptionsValidator : IValidateOptions<RequestLimitsOptions>
{
    private const long _minBytes = 1024;

    private const long _maxBytes = 30 * 1024 * 1024;

    public ValidateOptionsResult Validate(string? name, RequestLimitsOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (options.MaxRequestBodyBytes is < _minBytes or > _maxBytes)
        {
            return ValidateOptionsResult.Fail(
                $"'{RequestLimitsOptions.SectionName}:{nameof(RequestLimitsOptions.MaxRequestBodyBytes)}' " +
                $"is '{options.MaxRequestBodyBytes}', which must be between {_minBytes} and {_maxBytes} bytes " +
                "inclusive.");
        }

        return ValidateOptionsResult.Success;
    }
}

using Microsoft.Extensions.Options;

namespace AppTemplate.Api.Common.Hosting;

public sealed class RequestTimeoutsOptions
{
    public const string SectionName = "RequestTimeouts";

    public TimeSpan Default { get; set; } = TimeSpan.FromMinutes(5);
}

internal sealed class RequestTimeoutsOptionsValidator : IValidateOptions<RequestTimeoutsOptions>
{
    private static readonly TimeSpan _minTimeout = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan _maxTimeout = TimeSpan.FromHours(1);

    public ValidateOptionsResult Validate(string? name, RequestTimeoutsOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (options.Default < _minTimeout || options.Default > _maxTimeout)
        {
            return ValidateOptionsResult.Fail(
                $"'{RequestTimeoutsOptions.SectionName}:{nameof(RequestTimeoutsOptions.Default)}' " +
                $"must be between {_minTimeout} and {_maxTimeout}.");
        }

        return ValidateOptionsResult.Success;
    }
}

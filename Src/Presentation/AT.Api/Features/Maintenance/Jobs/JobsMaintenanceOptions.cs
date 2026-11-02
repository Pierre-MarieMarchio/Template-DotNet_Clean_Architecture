using Microsoft.Extensions.Options;

namespace AppTemplate.Api.Features.Maintenance.Jobs;

public sealed class JobsMaintenanceOptions
{
    public const string SectionName = "Jobs:Maintenance";

    public TimeSpan Interval { get; set; } = TimeSpan.FromHours(1);

    public bool PurgeExpiredIdempotencyKeysEnabled { get; set; } = true;
}

internal sealed class JobsMaintenanceOptionsValidator : IValidateOptions<JobsMaintenanceOptions>
{
    private static readonly TimeSpan _minimumInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan _maximumInterval = TimeSpan.FromDays(1);

    public ValidateOptionsResult Validate(string? name, JobsMaintenanceOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (options.Interval < _minimumInterval || options.Interval > _maximumInterval)
        {
            return ValidateOptionsResult.Fail(
                $"'{JobsMaintenanceOptions.SectionName}:Interval' must be between {_minimumInterval} and " +
                $"{_maximumInterval}.");
        }

        return ValidateOptionsResult.Success;
    }
}

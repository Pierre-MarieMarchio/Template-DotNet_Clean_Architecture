using Microsoft.Extensions.Options;

namespace AppTemplate.Api.Features.Files.Jobs;

public sealed class JobsFilesOptions
{
    public const string SectionName = "Jobs:Files";

    public TimeSpan PurgeAbandonedRegistrationsInterval { get; set; } = TimeSpan.FromHours(1);

    public bool PurgeAbandonedRegistrationsEnabled { get; set; } = true;

    public TimeSpan ReclaimOrphanedContentInterval { get; set; } = TimeSpan.FromHours(12);

    public bool ReclaimOrphanedContentEnabled { get; set; } = true;

    public TimeSpan InspectDepositedFilesInterval { get; set; } = TimeSpan.FromMinutes(1);

    public bool InspectDepositedFilesEnabled { get; set; } = true;
}

internal sealed class JobsFilesOptionsValidator : IValidateOptions<JobsFilesOptions>
{
    private static readonly TimeSpan _minimumPurgeInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan _maximumPurgeInterval = TimeSpan.FromDays(1);

    private static readonly TimeSpan _minimumReclaimInterval = TimeSpan.FromMinutes(1);

    private static readonly TimeSpan _maximumReclaimInterval = TimeSpan.FromDays(7);

    private static readonly TimeSpan _minimumInspectInterval = TimeSpan.FromSeconds(10);

    private static readonly TimeSpan _maximumInspectInterval = TimeSpan.FromHours(1);

    public ValidateOptionsResult Validate(string? name, JobsFilesOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (options.PurgeAbandonedRegistrationsInterval < _minimumPurgeInterval
            || options.PurgeAbandonedRegistrationsInterval > _maximumPurgeInterval)
        {
            return ValidateOptionsResult.Fail(
                $"'{JobsFilesOptions.SectionName}:PurgeAbandonedRegistrationsInterval' must be between " +
                $"{_minimumPurgeInterval} and {_maximumPurgeInterval}.");
        }

        if (options.ReclaimOrphanedContentInterval < _minimumReclaimInterval
            || options.ReclaimOrphanedContentInterval > _maximumReclaimInterval)
        {
            return ValidateOptionsResult.Fail(
                $"'{JobsFilesOptions.SectionName}:ReclaimOrphanedContentInterval' must be between " +
                $"{_minimumReclaimInterval} and {_maximumReclaimInterval}.");
        }

        if (options.InspectDepositedFilesInterval < _minimumInspectInterval
            || options.InspectDepositedFilesInterval > _maximumInspectInterval)
        {
            return ValidateOptionsResult.Fail(
                $"'{JobsFilesOptions.SectionName}:InspectDepositedFilesInterval' must be between " +
                $"{_minimumInspectInterval} and {_maximumInspectInterval}.");
        }

        return ValidateOptionsResult.Success;
    }
}

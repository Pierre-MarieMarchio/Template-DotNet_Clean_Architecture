using Microsoft.Extensions.Options;

namespace AppTemplate.Api.Features.Reminders.Jobs;

public sealed class JobsRemindersOptions
{
    public const string SectionName = "Jobs:Reminders";

    public TimeSpan Interval { get; set; } = TimeSpan.FromMinutes(1);

    public bool Enabled { get; set; } = true;
}

internal sealed class JobsRemindersOptionsValidator : IValidateOptions<JobsRemindersOptions>
{
    private static readonly TimeSpan _minimumInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan _maximumInterval = TimeSpan.FromHours(1);

    public ValidateOptionsResult Validate(string? name, JobsRemindersOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (options.Interval < _minimumInterval || options.Interval > _maximumInterval)
        {
            return ValidateOptionsResult.Fail(
                $"'{JobsRemindersOptions.SectionName}:Interval' must be between {_minimumInterval} and " +
                $"{_maximumInterval}.");
        }

        return ValidateOptionsResult.Success;
    }
}

using AppTemplate.Application.Features.Reminders.UseCases.Commands.FireDueReminders;
using Microsoft.Extensions.Options;

namespace AppTemplate.Api.Features.Reminders.Jobs;

/// <summary>
/// The reminder loop's cadence. No batch-size knob: <see cref="IFireDueRemindersUseCase"/> takes
/// no command at all — see its own doc for why it must stay that way — so how many reminders one
/// pass fires is <c>FireDueRemindersUseCase</c>'s own concern, not something this job
/// could pass through even if it wanted to.
/// </summary>
public sealed class JobsRemindersOptions
{
    public const string SectionName = "Jobs:Reminders";

    public TimeSpan Interval { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>
    /// Off switch for an operator who wants the rest of the process running without firing
    /// reminders — a maintenance window, or a deployment still validating the
    /// <c>IReminderNotifier</c> it wired in before letting it ring anyone for real.
    /// </summary>
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

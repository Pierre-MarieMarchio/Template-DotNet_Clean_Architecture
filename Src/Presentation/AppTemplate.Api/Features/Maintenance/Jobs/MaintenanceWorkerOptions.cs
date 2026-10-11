using Microsoft.Extensions.Options;

namespace AppTemplate.Api.Features.Maintenance.Jobs;

/// <summary>
/// The maintenance timer's cadence: how often it wakes up, and whether the idempotency purge runs
/// when it does. Every other purge owns its own switch under this same section — authentication's
/// two are <c>AuthPurgeOptions</c> — so an operator who runs one cleanup some other way is not
/// forced into the rest, and a feature removed from the template takes its keys with it.
/// </summary>
public sealed class MaintenanceWorkerOptions
{
    public const string SectionName = "MaintenanceWorker";

    /// <summary>How long to wait between iterations. The same value governs every task.</summary>
    public TimeSpan Interval { get; set; } = TimeSpan.FromHours(1);

    public bool PurgeExpiredIdempotencyKeysEnabled { get; set; } = true;
}

internal sealed class MaintenanceWorkerOptionsValidator : IValidateOptions<MaintenanceWorkerOptions>
{
    private static readonly TimeSpan _minimumInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan _maximumInterval = TimeSpan.FromDays(1);

    public ValidateOptionsResult Validate(string? name, MaintenanceWorkerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (options.Interval < _minimumInterval || options.Interval > _maximumInterval)
        {
            return ValidateOptionsResult.Fail(
                $"'{MaintenanceWorkerOptions.SectionName}:Interval' must be between {_minimumInterval} and " +
                $"{_maximumInterval}.");
        }

        return ValidateOptionsResult.Success;
    }
}

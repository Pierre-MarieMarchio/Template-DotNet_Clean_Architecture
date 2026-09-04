using Microsoft.Extensions.Options;

namespace AppTemplate.Worker.Features.Maintenance;

/// <summary>
/// The worker's cadence: how often it wakes up, and which of the two maintenance use cases it
/// runs when it does. Each task can be switched off independently, so an operator who wants the
/// idempotency purge but runs refresh-token cleanup some other way is not forced into both.
/// </summary>
public sealed class MaintenanceWorkerOptions
{
    public const string SectionName = "MaintenanceWorker";

    /// <summary>How long to wait between iterations. The same value governs both tasks.</summary>
    public TimeSpan Interval { get; set; } = TimeSpan.FromHours(1);

    public bool PurgeExpiredIdempotencyKeysEnabled { get; set; } = true;

    public bool PurgeExpiredRefreshTokensEnabled { get; set; } = true;
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

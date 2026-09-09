using System.Diagnostics;
using AppTemplate.Application.Auth.Features.Auth.UseCases.Commands.PurgeExpiredRefreshTokens;
using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Application.Core.Common.UseCases;
using AppTemplate.Application.Core.Features.Maintenance.UseCases.Commands.PurgeExpiredIdempotencyKeys;
using AppTemplate.Presentation.Core.Common.Jobs;
using AppTemplate.Worker.Common.Observability;
using Microsoft.Extensions.Options;

namespace AppTemplate.Worker.Features.Maintenance;

/// <summary>
/// Runs the two maintenance use cases on one timer, through the same
/// <see cref="IPurgeExpiredIdempotencyKeysUseCase"/> and <see cref="IPurgeExpiredRefreshTokensUseCase"/>
/// that <c>MaintenanceController</c> exposes over HTTP.
/// <para>
/// Both tasks share one <see cref="AsyncServiceScope"/> per iteration, and each is switched on or
/// off by its own option. A failing task is logged and retried at the next interval, and does not
/// stop its sibling in the same iteration.
/// </para>
/// </summary>
internal sealed class MaintenanceBackgroundService(
    IServiceScopeFactory scopeFactory,
    IOptions<MaintenanceWorkerOptions> options,
    ILogger<MaintenanceBackgroundService> logger) : BackgroundService
{
    private const string _idempotencyKeysTask = "expired idempotency keys";
    private const string _refreshTokenGrantsTask = "expired refresh-token grants";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;

        if (logger.IsEnabled(LogLevel.Information))
        {
            logger.LogInformation(
                "Maintenance worker starting. Interval: {Interval}. Idempotency purge enabled: " +
                "{IdempotencyEnabled}. Refresh-token purge enabled: {RefreshTokenEnabled}.",
                settings.Interval,
                settings.PurgeExpiredIdempotencyKeysEnabled,
                settings.PurgeExpiredRefreshTokensEnabled);
        }

        await PeriodicJob.RunAsync(
            settings.Interval,
            token => RunIterationAsync(settings, token),
            stoppingToken);

        logger.LogInformation("Maintenance worker stopping.");
    }

    private async Task RunIterationAsync(MaintenanceWorkerOptions settings, CancellationToken stoppingToken)
    {
        await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();

        await RunTaskAsync<IPurgeExpiredIdempotencyKeysUseCase>(
            scope.ServiceProvider,
            _idempotencyKeysTask,
            "Claimed keys will accumulate: nothing else removes an expired claim.",
            settings.PurgeExpiredIdempotencyKeysEnabled,
            stoppingToken);

        await RunTaskAsync<IPurgeExpiredRefreshTokensUseCase>(
            scope.ServiceProvider,
            _refreshTokenGrantsTask,
            "Rotated and expired grants will accumulate: nothing else removes them.",
            settings.PurgeExpiredRefreshTokensEnabled,
            stoppingToken);
    }

    /// <param name="disabledConsequence">What accumulates while the task is switched off, said on
    /// every skipped iteration. Logged at warning, like the file sweeps and unlike a reminder: what
    /// piles up here is rows nothing else in the system removes, and no user is waiting to notice.
    /// </param>
    private async Task RunTaskAsync<TUseCase>(
        IServiceProvider services,
        string label,
        string disabledConsequence,
        bool enabled,
        CancellationToken stoppingToken)
        where TUseCase : IUseCase<Result<int>>
    {
        KeyValuePair<string, object?> taskTag = new(WorkerTags.Task, label);

        if (!enabled)
        {
            MaintenanceInstruments.Iterations.Add(1, taskTag, new(WorkerTags.Outcome, "disabled"));

            logger.LogWarning(
                "The purge of {Label} is disabled; skipping this iteration. {Consequence}",
                label,
                disabledConsequence);

            return;
        }

        using Activity? activity = MaintenanceInstruments.ActivitySource.StartActivity("maintenance.purge");
        activity?.SetTag("maintenance.task", label);

        try
        {
            var useCase = services.GetRequiredService<TUseCase>();
            Result<int> result = await useCase.ExecuteAsync(stoppingToken);

            if (result.IsSuccess)
            {
                MaintenanceInstruments.Iterations.Add(1, taskTag, new(WorkerTags.Outcome, "success"));
                MaintenanceInstruments.Purged.Add(result.Value, taskTag);
                activity?.SetTag("maintenance.purged", result.Value);

                // Unconditional: a purge removing nothing for weeks because its query stopped
                // matching has to look different from a healthy one with nothing to do.
                if (logger.IsEnabled(LogLevel.Information))
                {
                    logger.LogInformation("Purge of {Label} completed: {Count} removed.", label, result.Value);
                }
            }
            else
            {
                Error error = result.Error!;
                MaintenanceInstruments.Iterations.Add(1, taskTag, new(WorkerTags.Outcome, "failure"));
                activity?.SetStatus(ActivityStatusCode.Error, error.Code);
                logger.LogWarning(
                    "Purging {Label} reported a failure: {ErrorCode} — {ErrorMessage}.",
                    label,
                    error.Code,
                    error.Message);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutdown, not a task failure: it ends the loop instead of being logged as an error.
            throw;
        }
        catch (Exception exception)
        {
            MaintenanceInstruments.Iterations.Add(1, taskTag, new(WorkerTags.Outcome, "exception"));
            activity?.SetStatus(ActivityStatusCode.Error, exception.GetType().Name);
            logger.LogError(exception, "Purging {Label} failed unexpectedly; will retry at the next interval.", label);
        }
    }
}

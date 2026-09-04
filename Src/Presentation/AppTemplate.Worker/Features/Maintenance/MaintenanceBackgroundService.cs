using System.Diagnostics;
using AppTemplate.Application.Common.Abstractions;
using AppTemplate.Application.Common.Results;
using AppTemplate.Application.Features.Maintenance.UseCases.Commands.PurgeExpiredIdempotencyKeys;
using AppTemplate.Application.Features.Maintenance.UseCases.Commands.PurgeExpiredRefreshTokens;
using Microsoft.Extensions.Options;

namespace AppTemplate.Worker.Features.Maintenance;

/// <summary>
/// Runs the two maintenance use cases on a timer, through the exact same
/// <see cref="IPurgeExpiredIdempotencyKeysUseCase"/> and <see cref="IPurgeExpiredRefreshTokensUseCase"/>
/// that <c>MaintenanceController</c> exposes over HTTP in AppTemplate.Api. Nothing about either use
/// case changes to be called from here — that is the entire point of this host existing.
/// <para>
/// Every iteration resolves its use cases from a fresh <see cref="AsyncServiceScope"/> rather than
/// injecting them directly: both use cases depend on scoped services (a <c>DbContext</c> or a
/// context factory), and holding one scope for the process lifetime would hold one set of those
/// captive for as long as the worker runs — the classic captive-dependency mistake, just easier to
/// miss here because nothing calls <c>Dispose</c> to surface it.
/// </para>
/// <para>
/// A failing iteration is logged and the loop tries again at the next interval; it never brings
/// the host down. A failure that only shows up once a month should not turn into an outage that
/// also stops every other maintenance task the same process runs.
/// </para>
/// </summary>
internal sealed class MaintenanceBackgroundService(
    IServiceScopeFactory scopeFactory,
    IOptions<MaintenanceWorkerOptions> options,
    ILogger<MaintenanceBackgroundService> logger) : BackgroundService
{
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

        using var timer = new PeriodicTimer(settings.Interval);

        do
        {
            await RunIterationAsync(settings, stoppingToken);
        }
        while (await WaitForNextTickAsync(timer, stoppingToken));

        logger.LogInformation("Maintenance worker stopping.");
    }

    /// <summary>
    /// Turns the timer's cancellation-on-shutdown into a plain "stop looping" rather than letting
    /// the exception unwind through the do/while below mid-iteration — the timer itself never fires
    /// mid-task, but treating its own cancellation as an ordinary false keeps the shutdown path in
    /// this one place instead of scattered across every caller.
    /// </summary>
    private static async Task<bool> WaitForNextTickAsync(PeriodicTimer timer, CancellationToken stoppingToken)
    {
        try
        {
            return await timer.WaitForNextTickAsync(stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    private async Task RunIterationAsync(MaintenanceWorkerOptions settings, CancellationToken stoppingToken)
    {
        await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();

        if (settings.PurgeExpiredIdempotencyKeysEnabled)
        {
            await RunTaskAsync<IPurgeExpiredIdempotencyKeysUseCase>(
                scope.ServiceProvider, "expired idempotency keys", stoppingToken);
        }

        if (settings.PurgeExpiredRefreshTokensEnabled)
        {
            await RunTaskAsync<IPurgeExpiredRefreshTokensUseCase>(
                scope.ServiceProvider, "expired refresh-token grants", stoppingToken);
        }
    }

    /// <summary>
    /// Runs one maintenance use case and isolates its failure from its sibling in the same
    /// iteration, the same way <c>DomainEventDispatcher</c> isolates one consumer's failure from
    /// the next: a broken idempotency purge must not also stop the refresh-token purge from
    /// running this cycle.
    /// </summary>
    private async Task RunTaskAsync<TUseCase>(IServiceProvider services, string label, CancellationToken stoppingToken)
        where TUseCase : IUseCase<Result<int>>
    {
        using Activity? activity = MaintenanceInstruments.ActivitySource.StartActivity("maintenance.purge");
        activity?.SetTag("maintenance.task", label);

        try
        {
            var useCase = services.GetRequiredService<TUseCase>();
            Result<int> result = await useCase.ExecuteAsync(stoppingToken);

            KeyValuePair<string, object?> taskTag = new("task", label);

            if (result.IsSuccess)
            {
                MaintenanceInstruments.Iterations.Add(1, taskTag, new("outcome", "success"));
                MaintenanceInstruments.Purged.Add(result.Value, taskTag);
                activity?.SetTag("maintenance.purged", result.Value);

                // Unconditional on purpose: without it, a purge that removes nothing for weeks
                // because its query silently stopped matching anything looks identical to a
                // healthy one — this line, and the iterations counter above, are what make "the
                // loop ran" visible even when there was nothing to do.
                if (logger.IsEnabled(LogLevel.Information))
                {
                    logger.LogInformation("Purge of {Label} completed: {Count} removed.", label, result.Value);
                }
            }
            else
            {
                Error error = result.Error!;
                MaintenanceInstruments.Iterations.Add(1, taskTag, new("outcome", "failure"));
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
            // Shutdown, not a task failure: let it propagate so the outer loop stops cleanly
            // instead of logging every graceful shutdown as an error.
            throw;
        }
        catch (Exception exception)
        {
            MaintenanceInstruments.Iterations.Add(1, new("task", label), new("outcome", "exception"));
            activity?.SetStatus(ActivityStatusCode.Error, exception.GetType().Name);
            logger.LogError(exception, "Purging {Label} failed unexpectedly; will retry at the next interval.", label);
        }
    }
}

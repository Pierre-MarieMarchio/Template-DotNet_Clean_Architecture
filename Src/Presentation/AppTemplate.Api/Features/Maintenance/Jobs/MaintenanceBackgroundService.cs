using System.Diagnostics;
using AppTemplate.Api.Common.Jobs;
using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Presentation.Core.Common.Jobs;
using Microsoft.Extensions.Options;

namespace AppTemplate.Api.Features.Maintenance.Jobs;

/// <summary>
/// Runs every registered <see cref="IMaintenanceTask"/> on one timer — every purge through the
/// use case that owns it, whether a request can also reach that use case or not.
/// <para>
/// All tasks share one background scope per iteration, and each is switched on or off by its own
/// option. A failing task is logged and retried at the next interval, and does not stop its
/// sibling in the same iteration. Which tasks exist is decided by what registers them: this class
/// names no feature.
/// </para>
/// </summary>
internal sealed class MaintenanceBackgroundService(
    IServiceScopeFactory scopeFactory,
    IOptions<JobsMaintenanceOptions> options,
    IEnumerable<IMaintenanceTask> tasks,
    ILogger<MaintenanceBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        var registered = tasks.ToList();

        if (logger.IsEnabled(LogLevel.Information))
        {
            logger.LogInformation(
                "Maintenance job starting. Interval: {Interval}. Purges enabled: {Purges}.",
                settings.Interval,
                string.Join(", ", registered.Select(task => $"{task.Label}={task.Enabled}")));
        }

        await PeriodicJob.RunAsync(
            settings.Interval,
            token => RunIterationAsync(registered, token),
            stoppingToken);

        logger.LogInformation("Maintenance job stopping.");
    }

    private async Task RunIterationAsync(List<IMaintenanceTask> registered, CancellationToken stoppingToken)
    {
        await using AsyncServiceScope scope = scopeFactory.CreateBackgroundScope();

        foreach (var task in registered)
        {
            await RunTaskAsync(task, scope.ServiceProvider, stoppingToken);
        }
    }

    /// <summary>
    /// A task that is switched off is logged at warning, like the file sweeps and unlike a reminder:
    /// what piles up here is rows nothing else in the system removes, and no user is waiting to
    /// notice. Its <see cref="IMaintenanceTask.DisabledConsequence"/> says what, on every skipped
    /// iteration.
    /// </summary>
    private async Task RunTaskAsync(
        IMaintenanceTask task,
        IServiceProvider services,
        CancellationToken stoppingToken)
    {
        string label = task.Label;
        KeyValuePair<string, object?> taskTag = new(JobTags.Task, label);

        if (!task.Enabled)
        {
            MaintenanceInstruments.Iterations.Add(1, taskTag, new(JobTags.Outcome, "disabled"));

            logger.LogWarning(
                "The purge of {Label} is disabled; skipping this iteration. {Consequence}",
                label,
                task.DisabledConsequence);

            return;
        }

        using Activity? activity = MaintenanceInstruments.ActivitySource.StartActivity("maintenance.purge");
        activity?.SetTag("maintenance.task", label);

        try
        {
            Result<int> result = await task.RunAsync(services, stoppingToken);

            if (result.IsSuccess)
            {
                MaintenanceInstruments.Iterations.Add(1, taskTag, new(JobTags.Outcome, "success"));
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
                MaintenanceInstruments.Iterations.Add(1, taskTag, new(JobTags.Outcome, "failure"));
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
            MaintenanceInstruments.Iterations.Add(1, taskTag, new(JobTags.Outcome, "exception"));
            activity?.SetStatus(ActivityStatusCode.Error, exception.GetType().Name);
            logger.LogError(exception, "Purging {Label} failed unexpectedly; will retry at the next interval.", label);
        }
    }
}

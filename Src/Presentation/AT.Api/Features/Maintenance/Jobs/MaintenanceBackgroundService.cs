using System.Diagnostics;
using AppTemplate.Api.Common.Jobs;
using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Presentation.Core.Common.Jobs;
using Microsoft.Extensions.Options;

namespace AppTemplate.Api.Features.Maintenance.Jobs;

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

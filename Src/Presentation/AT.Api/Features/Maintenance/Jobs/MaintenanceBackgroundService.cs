using AppTemplate.Presentation.Core.Common.Jobs;
using Microsoft.Extensions.Options;

namespace AppTemplate.Api.Features.Maintenance.Jobs;

internal sealed class MaintenanceBackgroundService(
    IServiceScopeFactory scopeFactory,
    IOptions<JobsMaintenanceOptions> options,
    IEnumerable<IMaintenanceTask> tasks,
    ILogger<MaintenanceBackgroundService> logger,
    TimeProvider timeProvider) : BackgroundService
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
                string.Join(", ", registered.Select(task => $"{task.Pass.Label}={task.Enabled}")));
        }

        await PeriodicJob.RunAsync(
            settings.Interval,
            timeProvider,
            token => RunIterationAsync(registered, token),
            stoppingToken);

        logger.LogInformation("Maintenance job stopping.");
    }

    private async Task RunIterationAsync(List<IMaintenanceTask> registered, CancellationToken stoppingToken)
    {
        await using AsyncServiceScope scope = scopeFactory.CreateBackgroundScope();

        foreach (var task in registered)
        {
            if (task.Enabled)
            {
                await task.SendAsync(scope.ServiceProvider, logger, stoppingToken);
            }
            else
            {
                task.Pass.SkipDisabled(logger);
            }
        }
    }
}

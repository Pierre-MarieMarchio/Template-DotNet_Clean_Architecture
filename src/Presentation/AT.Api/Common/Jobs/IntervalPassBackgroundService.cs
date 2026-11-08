using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Presentation.Core.Common.Jobs;
using Mediator;

namespace AppTemplate.Api.Common.Jobs;

internal sealed class IntervalPassBackgroundService<TCommand>(
    IntervalPass schedule,
    IServiceScopeFactory scopeFactory,
    ILogger<IntervalPassBackgroundService<TCommand>> logger,
    TimeProvider timeProvider) : BackgroundService
    where TCommand : ICommand<Result<int>>, new()
{
    internal IntervalPass Schedule => schedule;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (logger.IsEnabled(LogLevel.Information))
        {
            logger.LogInformation(
                "The pass over {Label} is starting. Interval: {Interval}. Enabled: {Enabled}.",
                schedule.Pass.Label,
                schedule.Interval,
                schedule.Enabled);
        }

        await PeriodicJob.RunAsync(schedule.Interval, timeProvider, RunOnceAsync, stoppingToken);

        if (logger.IsEnabled(LogLevel.Information))
        {
            logger.LogInformation("The pass over {Label} is stopping.", schedule.Pass.Label);
        }
    }

    private async Task RunOnceAsync(CancellationToken stoppingToken)
    {
        if (!schedule.Enabled)
        {
            schedule.Pass.SkipDisabled(logger);
            return;
        }

        await using AsyncServiceScope scope = scopeFactory.CreateBackgroundScope();

        await schedule.Pass.SendAsync<TCommand>(scope.ServiceProvider, logger, stoppingToken);
    }
}

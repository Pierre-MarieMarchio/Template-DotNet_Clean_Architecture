using System.Diagnostics;
using AppTemplate.Api.Common.Jobs;
using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Application.Features.Reminders.UseCases.Commands.FireDueReminders;
using AppTemplate.Presentation.Core.Common.Jobs;
using Microsoft.Extensions.Options;

namespace AppTemplate.Api.Features.Reminders.Jobs;

internal sealed class ReminderBackgroundService(
    IServiceScopeFactory scopeFactory,
    IOptions<JobsRemindersOptions> options,
    ILogger<ReminderBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;

        if (logger.IsEnabled(LogLevel.Information))
        {
            logger.LogInformation(
                "Reminder job starting. Interval: {Interval}. Enabled: {Enabled}.",
                settings.Interval,
                settings.Enabled);
        }

        await PeriodicJob.RunAsync(
            settings.Interval,
            token => RunIterationAsync(settings, token),
            stoppingToken);

        logger.LogInformation("Reminder job stopping.");
    }

    private async Task RunIterationAsync(JobsRemindersOptions settings, CancellationToken stoppingToken)
    {
        if (!settings.Enabled)
        {
            ReminderInstruments.Iterations.Add(1, new KeyValuePair<string, object?>(JobTags.Outcome, "disabled"));

            if (logger.IsEnabled(LogLevel.Information))
            {
                logger.LogInformation("Reminder firing is disabled; skipping this pass.");
            }

            return;
        }

        await using AsyncServiceScope scope = scopeFactory.CreateBackgroundScope();

        using Activity? activity = ReminderInstruments.ActivitySource.StartActivity("reminders.fire");

        try
        {
            var useCase = scope.ServiceProvider.GetRequiredService<IFireDueRemindersUseCase>();
            Result<int> result = await useCase.ExecuteAsync(stoppingToken);

            if (result.IsSuccess)
            {
                ReminderInstruments.Iterations.Add(1, new KeyValuePair<string, object?>(JobTags.Outcome, "success"));
                ReminderInstruments.Notified.Add(result.Value);
                activity?.SetTag("reminders.notified", result.Value);

                if (logger.IsEnabled(LogLevel.Information))
                {
                    logger.LogInformation("Reminder pass completed: {Count} notified.", result.Value);
                }
            }
            else
            {
                Error error = result.Error!;
                ReminderInstruments.Iterations.Add(1, new KeyValuePair<string, object?>(JobTags.Outcome, "failure"));
                activity?.SetStatus(ActivityStatusCode.Error, error.Code);
                logger.LogWarning(
                    "Firing due reminders reported a failure: {ErrorCode} — {ErrorMessage}.",
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
            ReminderInstruments.Iterations.Add(1, new KeyValuePair<string, object?>(JobTags.Outcome, "exception"));
            activity?.SetStatus(ActivityStatusCode.Error, exception.GetType().Name);
            logger.LogError(exception, "Firing due reminders failed unexpectedly; will retry at the next interval.");
        }
    }
}

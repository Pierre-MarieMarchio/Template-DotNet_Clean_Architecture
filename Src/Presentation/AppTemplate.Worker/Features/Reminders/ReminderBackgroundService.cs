using System.Diagnostics;
using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Application.Features.Reminders.UseCases.Commands.FireDueReminders;
using AppTemplate.Presentation.Core.Common.Jobs;
using Microsoft.Extensions.Options;

namespace AppTemplate.Worker.Features.Reminders;

/// <summary>
/// Runs <see cref="IFireDueRemindersUseCase"/> on a timer — the only caller it ever has, since it
/// must never run behind a request.
/// <para>
/// One pass takes one <see cref="AsyncServiceScope"/>, and the whole pass is switched on or off by
/// a single option. A failing pass is logged and retried at the next tick.
/// </para>
/// </summary>
internal sealed class ReminderBackgroundService(
    IServiceScopeFactory scopeFactory,
    IOptions<ReminderWorkerOptions> options,
    ILogger<ReminderBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;

        if (logger.IsEnabled(LogLevel.Information))
        {
            logger.LogInformation(
                "Reminder worker starting. Interval: {Interval}. Enabled: {Enabled}.",
                settings.Interval,
                settings.Enabled);
        }

        await PeriodicJob.RunAsync(
            settings.Interval,
            token => RunIterationAsync(settings, token),
            stoppingToken);

        logger.LogInformation("Reminder worker stopping.");
    }

    private async Task RunIterationAsync(ReminderWorkerOptions settings, CancellationToken stoppingToken)
    {
        if (!settings.Enabled)
        {
            // Counted and logged every time: a loop switched off by configuration has to look
            // different both from a healthy quiet pass and from a loop that died.
            ReminderInstruments.Iterations.Add(1, new KeyValuePair<string, object?>("outcome", "disabled"));

            if (logger.IsEnabled(LogLevel.Information))
            {
                logger.LogInformation("Reminder firing is disabled; skipping this pass.");
            }

            return;
        }

        await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();

        using Activity? activity = ReminderInstruments.ActivitySource.StartActivity("reminders.fire");

        try
        {
            var useCase = scope.ServiceProvider.GetRequiredService<IFireDueRemindersUseCase>();
            Result<int> result = await useCase.ExecuteAsync(stoppingToken);

            if (result.IsSuccess)
            {
                // Unconditional: a pass that notified nobody for days because the due-date query
                // stopped matching has to look different from one that simply had nothing due.
                ReminderInstruments.Iterations.Add(1, new KeyValuePair<string, object?>("outcome", "success"));
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
                ReminderInstruments.Iterations.Add(1, new KeyValuePair<string, object?>("outcome", "failure"));
                activity?.SetStatus(ActivityStatusCode.Error, error.Code);
                logger.LogWarning(
                    "Firing due reminders reported a failure: {ErrorCode} — {ErrorMessage}.",
                    error.Code,
                    error.Message);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutdown, not a failed pass: it ends the loop instead of being logged as an error.
            throw;
        }
        catch (Exception exception)
        {
            ReminderInstruments.Iterations.Add(1, new KeyValuePair<string, object?>("outcome", "exception"));
            activity?.SetStatus(ActivityStatusCode.Error, exception.GetType().Name);
            logger.LogError(exception, "Firing due reminders failed unexpectedly; will retry at the next interval.");
        }
    }
}

using System.Diagnostics;
using AppTemplate.Application.Common.Results;
using AppTemplate.Application.Features.Reminders.UseCases.Commands.FireDueReminders;
using Microsoft.Extensions.Options;

namespace AppTemplate.Worker.Features.Reminders;

/// <summary>
/// Runs <see cref="IFireDueRemindersUseCase"/> on a timer — the only caller this use case ever has,
/// per its own doc, since it must never run behind a request. Modelled on
/// <c>MaintenanceBackgroundService</c>: a fresh scope per iteration (the use case depends on a
/// <c>DbContext</c>), the stopping token honoured rather than waited out, and a failing iteration
/// logged and retried at the next tick instead of bringing the host down.
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

        using var timer = new PeriodicTimer(settings.Interval);

        do
        {
            await RunIterationAsync(settings, stoppingToken);
        }
        while (await WaitForNextTickAsync(timer, stoppingToken));

        logger.LogInformation("Reminder worker stopping.");
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

    private async Task RunIterationAsync(ReminderWorkerOptions settings, CancellationToken stoppingToken)
    {
        if (!settings.Enabled)
        {
            // Counted, not skipped: a loop switched off by configuration is a running loop that
            // decided to do nothing, and it has to look different both from a healthy quiet pass and
            // from a loop that died. Logged every time for the same reason — see the log inside the
            // try block below for the other half of that requirement.
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
                // silently stopped matching anything must look different from a healthy pass that
                // simply had nothing due, and only this line — logged every time, count included —
                // makes that visible.
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
            // Shutdown, not a failed pass: let it propagate so the outer loop stops cleanly instead
            // of logging a graceful shutdown as an error.
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

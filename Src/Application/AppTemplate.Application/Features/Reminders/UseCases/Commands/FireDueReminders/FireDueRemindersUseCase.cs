using AppTemplate.Application.Core.Common.Concurrency;
using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Application.Features.Reminders.Ports.ReminderDiagnostics;
using AppTemplate.Application.Features.Reminders.Ports.ReminderNotifier;
using AppTemplate.Application.Features.Reminders.Ports.ReminderRecipients;
using AppTemplate.Application.Features.TodoLists.Ports.TodoItemQueries;
using AppTemplate.Domain.Features.Reminders.Model.Entities;
using AppTemplate.Domain.Features.Reminders.Repositories;
using Microsoft.Extensions.Logging;

namespace AppTemplate.Application.Features.Reminders.UseCases.Commands.FireDueReminders;

public sealed class FireDueRemindersUseCase(
    IReminderRepository reminders,
    ITodoItemQueries todoItems,
    IReminderNotifier notifier,
    IReminderRecipientDirectory recipients,
    IReminderDiagnostics diagnostics,
    IUnitOfWork unitOfWork,
    IDateTimeProvider dateTimeProvider,
    ILeaderLease lease,
    ILogger<FireDueRemindersUseCase> logger) : IFireDueRemindersUseCase
{
    private const int _batchSize = 200;

    private const string _leaseName = "apptemplate.reminders.fire-due";

    public async Task<Result<int>> ExecuteAsync(CancellationToken cancellationToken = default)
    {
        int notified = 0;

        bool ranHere = await lease.TryRunExclusivelyAsync(
            _leaseName,
            async cancellationToken => notified = await FireDueBatchAsync(cancellationToken),
            cancellationToken);

        if (!ranHere && logger.IsEnabled(LogLevel.Information))
        {
            logger.LogInformation(
                "Reminder pass skipped: another host holds the '{LeaseName}' lease.",
                _leaseName);
        }

        return notified;
    }

    private async Task<int> FireDueBatchAsync(CancellationToken cancellationToken)
    {
        var now = dateTimeProvider.UtcNow;
        var due = await reminders.GetDueAsync(now, _batchSize, cancellationToken);

        if (due.Count == 0)
        {
            return 0;
        }

        Guid[] todoItemIds = [.. due.Select(reminder => reminder.TodoItemId).Distinct()];
        var completionStates = await todoItems.GetCompletionStatesAsync(todoItemIds, cancellationToken);

        int notified = 0;

        foreach (var reminder in due)
        {
            var outcome = await FireAsync(reminder, completionStates, now, cancellationToken);

            if (outcome == Outcome.Notified)
            {
                notified++;
            }
            else if (outcome == Outcome.Conflicted)
            {
                break;
            }
        }

        return notified;
    }

    private async Task<Outcome> FireAsync(
        Reminder reminder,
        IReadOnlyDictionary<Guid, bool> completionStates,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        bool targetExists = completionStates.TryGetValue(reminder.TodoItemId, out bool isCompleted);

        if (!targetExists || isCompleted)
        {
            reminder.Cancel();

            if (!await TryCommitAsync(reminder, cancellationToken))
            {
                return Outcome.Conflicted;
            }

            if (targetExists)
            {
                diagnostics.RecordMissedCancellation();
            }

            return Outcome.Settled;
        }

        var recipient = await recipients.FindAsync(reminder.OwnerId, cancellationToken);

        if (recipient is null)
        {
            logger.LogWarning(
                "Reminder {ReminderId} has no account to notify; owner {OwnerId} is gone.",
                reminder.Id,
                reminder.OwnerId);

            reminder.Cancel();

            return await TryCommitAsync(reminder, cancellationToken) ? Outcome.Settled : Outcome.Conflicted;
        }

        try
        {
            await notifier.NotifyAsync(
                new ReminderNotification(reminder.OwnerId, recipient, reminder.TodoItemId, reminder.DueAt),
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            reminder.RecordFailure(now);

            logger.LogWarning(
                exception,
                "Notifying reminder {ReminderId} for owner {OwnerId} failed (attempt {FailedAttempts}); " +
                "it will not be tried again before {NextAttemptAt}.",
                reminder.Id,
                reminder.OwnerId,
                reminder.FailedAttempts,
                reminder.NextAttemptAt);

            return await TryCommitAsync(reminder, cancellationToken) ? Outcome.Settled : Outcome.Conflicted;
        }

        reminder.MarkNotified(now);

        return await TryCommitAsync(reminder, cancellationToken) ? Outcome.Notified : Outcome.Conflicted;
    }

    private async Task<bool> TryCommitAsync(Reminder reminder, CancellationToken cancellationToken)
    {
        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);

            return true;
        }
        catch (ConcurrencyConflictException exception)
        {
            logger.LogWarning(
                exception,
                "Reminder {ReminderId} was changed by somebody else while it was being fired; this pass " +
                "ends here and the next one reads it afresh.",
                reminder.Id);

            return false;
        }
    }

    private enum Outcome
    {
        Settled,

        Notified,

        Conflicted,
    }
}

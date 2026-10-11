using AppTemplate.Application.Core.Common.Concurrency;
using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Application.Features.Reminders.Ports.ReminderDiagnostics;
using AppTemplate.Application.Features.Reminders.Ports.ReminderNotifier;
using AppTemplate.Application.Features.Reminders.Ports.ReminderRecipients;
using AppTemplate.Application.Features.TodoLists.Ports.TodoItemQueries;
using AppTemplate.Domain.Features.Reminders.Entities;
using AppTemplate.Domain.Features.Reminders.Repositories;
using Microsoft.Extensions.Logging;

namespace AppTemplate.Application.Features.Reminders.UseCases.Commands.FireDueReminders;

/// <summary>
/// Runs on a schedule, from the API's reminder job — never from a request — and so must not read
/// <see cref="ICurrentUser"/>: a background scope's <c>NoCallerCurrentUser</c> throws on
/// <c>UserId</c> rather than pretend an anonymous caller. The whole pass runs under
/// <see cref="ILeaderLease"/>, with no per-reminder claim on top of it; delivery is at-least-once
/// and a failure costs one reminder. The retry guarantee — what each failure costs, why a
/// conflict ends the pass, why a failed notification is never abandoned, and why completion is
/// re-checked here rather than trusted from the cancelling consumer — is stated in
/// <c>docs/DECISIONS.md</c>.
/// </summary>
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
    /// <summary>Caps one pass to a bounded amount of work; a backlog beyond this is picked up by
    /// the next run rather than loaded all at once.</summary>
    private const int _batchSize = 200;

    /// <summary>Names the exclusion; fixed for the life of the application.</summary>
    /// <remarks>
    /// The lock key is derived from this string, so during a rolling deploy every host that must
    /// contend has to spell it identically. Renaming it would give two releases different keys,
    /// each uncontended, and both would fire the same batch believing itself the leader.
    /// </remarks>
    private const string _leaseName = "apptemplate.reminders.fire-due";

    public async Task<Result<int>> ExecuteAsync(CancellationToken cancellationToken = default)
    {
        // The lease takes work that returns nothing, so the count comes back through a local the
        // delegate closes over: assigned at most once, and still zero on the standby path because
        // the delegate never ran.
        int notified = 0;

        bool ranHere = await lease.TryRunExclusivelyAsync(
            _leaseName,
            async leaseToken => notified = await FireDueBatchAsync(leaseToken),
            cancellationToken);

        if (!ranHere && logger.IsEnabled(LogLevel.Information))
        {
            // A standby pass and a pass with nothing due both report zero, and this line is what
            // tells them apart in the log.
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

        // One query for the whole batch, rather than one per reminder.
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

    /// <summary>One reminder, from the decision to its commit.</summary>
    private async Task<Outcome> FireAsync(
        Reminder reminder,
        IReadOnlyDictionary<Guid, bool> completionStates,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        bool targetExists = completionStates.TryGetValue(reminder.TodoItemId, out bool isCompleted);

        if (!targetExists || isCompleted)
        {
            // An absent item was removed, or its list deleted; neither raises an event, so nothing
            // was missed. A completed one is a cancellation that never arrived: GetDueAsync returns
            // only Pending reminders, so the completion event did not reach its consumer.
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
            // A cancelled run is not a failed notification.
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

    /// <returns><c>false</c> when somebody else changed the reminder first, in which case nothing
    /// was written and the failed write is still staged in this scope.</returns>
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
        /// <summary>Handled and committed, without a notification going out: cancelled, failed or left alone.</summary>
        Settled,

        /// <summary>Notified and committed.</summary>
        Notified,

        /// <summary>Its commit lost a race, so the pass has to stop.</summary>
        Conflicted,
    }
}

using AppTemplate.Application.Core.Common.Concurrency;
using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Application.Features.Reminders.Ports.ReminderDiagnostics;
using AppTemplate.Application.Features.Reminders.Ports.ReminderNotifier;
using AppTemplate.Application.Features.TodoLists.Ports.TodoItemQueries;
using AppTemplate.Domain.Features.Reminders.Entities;
using AppTemplate.Domain.Features.Reminders.Repositories;
using Microsoft.Extensions.Logging;

namespace AppTemplate.Application.Features.Reminders.UseCases.Commands.FireDueReminders;

/// <summary>
/// Runs on a schedule, from the API's reminder job — never from a request — which is why it
/// must not read <see cref="ICurrentUser"/>: a background scope's <c>NoCallerCurrentUser</c> throws on
/// <c>UserId</c> rather than pretend an anonymous caller, and a use case that reads it would fail
/// on its very first iteration.
/// <para>
/// <b>One host at a time.</b> The whole pass runs under <see cref="ILeaderLease"/>, and that lease
/// is the only thing keeping two replicas from delivering the same reminder twice: the per-reminder
/// claim does not, because <c>Reminder.TryClaim</c> mutates in memory and the batch commits at the
/// end, so both passes read the claim as free and both send the mail before one of them loses on
/// <c>xmin</c>. Finding the lease taken returns zero notified and says so in the log, because zero
/// on its own would be indistinguishable from a pass that had nothing due.
/// </para>
/// <para>
/// <b>Delivery is at-least-once, and what a failure costs is one reminder.</b> Each reminder is
/// notified and then committed on its own, so a commit that fails loses that reminder's outcome and
/// nothing else: the ones before it are already <c>Fired</c> and are never sent again. A crash
/// between <see cref="IReminderNotifier.NotifyAsync"/> succeeding and that reminder's commit leaves
/// it <c>Pending</c>, and the next pass sends it again. The lease narrows the window in which two
/// hosts could both do this; it is not a fencing token, so leadership can lapse mid-pass without
/// this code being told, and a second host may then fire what this one is firing.
/// </para>
/// <para>
/// <b>A conflict ends the pass.</b> A reminder changed by somebody else — cancelled, say, while its
/// notification was in flight — makes its commit fail, and the failed write stays staged in this
/// scope, so every later save would meet the same conflict. The pass therefore stops there and the
/// reminders after it wait for the next one, which reads the conflicting reminder afresh: a cancelled
/// one is no longer due. A notification already sent for it stays sent, as the race allows.
/// </para>
/// <para>
/// <b>A failed notification is retried, never abandoned, and never at the cost of the others.</b>
/// It is recorded on the reminder, which waits a minute before its next attempt and doubles the wait
/// up to an hour. Reminders come back in the order they became eligible, so one that keeps failing
/// queues behind those that have waited longer instead of filling every batch. It is retried for as
/// long as its item is outstanding; there is no state in which it is given up on.
/// </para>
/// <para>
/// <b>Completion is re-checked here, not trusted from the event that should have cancelled the
/// reminder already.</b> <c>CancelRemindersOnTodoItemCompletedConsumer</c> runs outside the
/// transaction that completed the item and is not retried if it is missed, so this check is what
/// makes firing correct independently of whether that delivery happened. Removing it makes that
/// consumer load-bearing.
/// </para>
/// <para>
/// <b>An id absent from <see cref="ITodoItemQueries.GetCompletionStatesAsync"/>'s result is
/// cancelled here exactly like a completed one.</b> Removing a to-do item or deleting its list
/// raises no domain event, and none is needed: the orphaned reminder is retired the next time it
/// comes up for firing, which is also why it is not counted as a missed cancellation — see
/// <see cref="IReminderDiagnostics"/>.
/// </para>
/// </summary>
public sealed class FireDueRemindersUseCase(
    IReminderRepository reminders,
    ITodoItemQueries todoItems,
    IReminderNotifier notifier,
    IReminderDiagnostics diagnostics,
    IUnitOfWork unitOfWork,
    IDateTimeProvider dateTimeProvider,
    ILeaderLease lease,
    ILogger<FireDueRemindersUseCase> logger) : IFireDueRemindersUseCase
{
    /// <summary>Caps one pass to a bounded amount of work; a backlog beyond this is picked up by
    /// the next run rather than loaded all at once.</summary>
    private const int _batchSize = 200;

    /// <summary>
    /// Names the exclusion, and is fixed for the life of the application.
    /// </summary>
    /// <remarks>
    /// The lock key is derived from this string, so every host that must contend has to spell it
    /// identically — and during a rolling deploy the hosts contending are two releases of this
    /// binary at once. Renaming it would give them two different keys, each uncontended, and both
    /// would fire the same batch believing itself the leader: the exact failure this exists to
    /// prevent, occurring precisely while the code that prevents it is being deployed. Nothing in
    /// the build can see that, which is why it is written here rather than left to be inferred.
    /// </remarks>
    private const string _leaseName = "apptemplate.reminders.fire-due";

    /// <summary>How long a claim is honoured before a host that died mid-attempt is presumed gone
    /// and another host may retry the same reminder.</summary>
    private static readonly TimeSpan _staleClaimAfter = TimeSpan.FromMinutes(5);

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

        if (!reminder.TryClaim(now, _staleClaimAfter))
        {
            return Outcome.Settled;
        }

        try
        {
            await notifier.NotifyAsync(
                new ReminderNotification(reminder.OwnerId, reminder.TodoItemId, reminder.DueAt),
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // A cancelled run is not a failed notification.
            throw;
        }
        catch (Exception exception)
        {
            reminder.ReleaseClaim();
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

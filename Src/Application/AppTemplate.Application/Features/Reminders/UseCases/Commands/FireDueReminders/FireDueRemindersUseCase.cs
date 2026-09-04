using AppTemplate.Application.Common.Abstractions;
using AppTemplate.Application.Common.Results;
using AppTemplate.Application.Features.Reminders.Ports.ReminderDiagnostics;
using AppTemplate.Application.Features.Reminders.Ports.ReminderNotifier;
using AppTemplate.Application.Features.Reminders.Ports.ReminderTargetQueries;
using AppTemplate.Domain.Features.Reminders.Repositories;
using Microsoft.Extensions.Logging;

namespace AppTemplate.Application.Features.Reminders.UseCases.Commands.FireDueReminders;

/// <summary>
/// Runs on a schedule, from <c>AppTemplate.Worker</c> — never from a request — which is why it
/// must not read <see cref="ICurrentUser"/>: the worker's <c>BackgroundCurrentUser</c> throws on
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
/// <b>Delivery is at-least-once.</b> A crash between <see cref="IReminderNotifier.NotifyAsync"/>
/// succeeding and this use case's own commit lands the reminder back in the next pass's batch,
/// still <c>Pending</c>, and it fires again. The lease narrows that window; it does not remove it.
/// </para>
/// <para>
/// <b>Completion is re-checked here, not trusted from the event that should have cancelled the
/// reminder already.</b> <c>CancelRemindersOnTodoItemCompletedConsumer</c> runs outside the
/// transaction that completed the item and is not retried if it is missed, so this check is what
/// makes firing correct independently of whether that delivery happened. Removing it makes that
/// consumer load-bearing.
/// </para>
/// <para>
/// <b>An id absent from <see cref="IReminderTargetQueries.GetCompletionStatesAsync"/>'s result is
/// cancelled here exactly like a completed one.</b> Removing a to-do item or deleting its list
/// raises no domain event, and none is needed: the orphaned reminder is retired the next time it
/// comes up for firing, which is also why it is not counted as a missed cancellation — see
/// <see cref="IReminderDiagnostics"/>.
/// </para>
/// </summary>
public sealed class FireDueRemindersUseCase(
    IReminderRepository reminders,
    IReminderTargetQueries targets,
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
        // The lease takes work that returns nothing while this use case owes its caller a count, so
        // the count comes back through a local the delegate closes over. It is assigned at most
        // once — the port runs the work once per call, awaited on this path — so there is nothing
        // to synchronise, and it is still zero on the standby path exactly because the delegate
        // never ran. An out parameter cannot cross an async lambda and a field would make one
        // instance's count visible to another's pass; the local is what has neither problem.
        int notified = 0;

        bool ranHere = await lease.TryRunExclusivelyAsync(
            _leaseName,
            async leaseToken => notified = await FireDueBatchAsync(leaseToken),
            cancellationToken);

        if (!ranHere && logger.IsEnabled(LogLevel.Information))
        {
            // A standby pass and a pass with nothing due both come back as zero, and
            // ReminderBackgroundService logs that count on every single pass so that "nothing has
            // fired for days" cannot be mistaken for "nothing was due". This line is what keeps the
            // third way of reaching zero from collapsing into the other two.
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

        // One query for the whole batch: re-checking completion reminder by reminder would be
        // exactly the round-trip cost GetDueAsync's own batching exists to avoid.
        Guid[] todoItemIds = [.. due.Select(reminder => reminder.TodoItemId).Distinct()];
        var completionStates = await targets.GetCompletionStatesAsync(todoItemIds, cancellationToken);

        int notified = 0;

        foreach (var reminder in due)
        {
            bool targetExists = completionStates.TryGetValue(reminder.TodoItemId, out bool isCompleted);

            if (targetExists && isCompleted)
            {
                // Everything GetDueAsync returns is Pending by contract, so an item already
                // complete here is always a cancellation that never arrived: the completion event
                // did not reach the consumer that watches for it, and this count is exactly that
                // loss. Cancel() would throw on anything already fired, which is the same
                // assumption stated twice — guarding one and not the other would only hide a
                // broken contract behind a wrong number.
                diagnostics.RecordMissedCancellation();
                reminder.Cancel();

                continue;
            }

            if (!targetExists)
            {
                // The item was removed, or its list was deleted — no event to have missed, since
                // neither raises one. Not a divergence: this is the mechanism working as intended.
                reminder.Cancel();

                continue;
            }

            if (!reminder.TryClaim(now, _staleClaimAfter))
            {
                continue;
            }

            try
            {
                await notifier.NotifyAsync(
                    new ReminderNotification(reminder.OwnerId, reminder.TodoItemId, reminder.DueAt),
                    cancellationToken);

                reminder.MarkNotified(now);
                notified++;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // A cancelled run is not a failed notification: rethrowing keeps cancellation
                // honest instead of logging it as one.
                throw;
            }
            catch (Exception exception)
            {
                // Released rather than left to expire on its own, so the next pass retries this
                // reminder immediately instead of waiting out the staleness window for nothing.
                // The rest of the batch still runs: one owner's broken notification channel must
                // not delay every other reminder due in the same pass.
                reminder.ReleaseClaim();

                logger.LogWarning(
                    exception,
                    "Notifying reminder {ReminderId} for owner {OwnerId} failed; the claim was " +
                    "released for the next pass to retry.",
                    reminder.Id,
                    reminder.OwnerId);
            }
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return notified;
    }
}

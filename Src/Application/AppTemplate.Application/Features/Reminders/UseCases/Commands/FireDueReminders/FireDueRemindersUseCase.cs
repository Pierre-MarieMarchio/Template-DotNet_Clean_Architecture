using AppTemplate.Application.Common;
using AppTemplate.Application.Common.Abstractions;
using AppTemplate.Application.Features.Reminders.Ports.ReminderDiagnostics;
using AppTemplate.Application.Features.Reminders.Ports.ReminderNotifier;
using AppTemplate.Application.Features.Reminders.Ports.ReminderTargets;
using AppTemplate.Domain.Features.Reminders.Repositories;
using AppTemplate.Domain.Features.Reminders.ValueObjects;
using Microsoft.Extensions.Logging;

namespace AppTemplate.Application.Features.Reminders.UseCases.Commands.FireDueReminders;

/// <summary>
/// Runs on a schedule, from <c>AppTemplate.Worker</c> — never from a request — which is why it
/// must not read <see cref="ICurrentUser"/>: the worker's <c>BackgroundCurrentUser</c> throws on
/// <c>UserId</c> rather than pretend an anonymous caller, and a use case that reads it would fail
/// on its very first iteration.
/// <para>
/// <b>Delivery is at-least-once, on purpose.</b> A crash between <see cref="IReminderNotifier.NotifyAsync"/>
/// succeeding and this use case's own commit lands the reminder back in the next pass's batch,
/// still <c>Pending</c>, and it fires again. For a reminder a duplicate is harmless and a silent
/// drop is not, so the trade is made deliberately in that direction rather than paid for an
/// exactly-once guarantee this mechanism does not need.
/// </para>
/// <para>
/// <b>Completion is re-checked here, not trusted from the event that should have cancelled the
/// reminder already.</b> <c>CancelRemindersOnTodoItemCompletedConsumer</c> runs outside the
/// transaction that completed the item (see
/// <c>DomainEventDispatchSaveChangesInterceptor</c>) and is not retried if it is missed, so this
/// is what makes firing correct independently of whether that delivery happened. That consumer is
/// a fast path, not the correctness guarantee: it retires a reminder as soon as its item is
/// completed instead of waiting for the reminder to come due, but nothing here depends on it
/// having run.
/// </para>
/// <para>
/// <b>A deleted item needs no domain event to be handled correctly, for the same reason.</b>
/// Removing a to-do item or deleting its list raises nothing (see the domain — only creation,
/// completion and reopening do), and none is needed: an id absent from
/// <see cref="IReminderTargets.GetCompletionStatesAsync"/>'s result is cancelled here exactly
/// like a completed one, the next time this reminder comes up for firing. An orphaned reminder is
/// self-healing on its own schedule; that is also why it is not counted as a missed cancellation —
/// see <see cref="IReminderDiagnostics"/>.
/// </para>
/// </summary>
public sealed class FireDueRemindersUseCase(
    IReminderRepository reminders,
    IReminderTargets targets,
    IReminderNotifier notifier,
    IReminderDiagnostics diagnostics,
    IUnitOfWork unitOfWork,
    IDateTimeProvider dateTimeProvider,
    ILogger<FireDueRemindersUseCase> logger) : IFireDueRemindersUseCase
{
    /// <summary>Caps one pass to a bounded amount of work; a backlog beyond this is picked up by
    /// the next run rather than loaded all at once.</summary>
    private const int _batchSize = 200;

    /// <summary>How long a claim is honoured before a host that died mid-attempt is presumed gone
    /// and another host may retry the same reminder.</summary>
    private static readonly TimeSpan _staleClaimAfter = TimeSpan.FromMinutes(5);

    public async Task<Result<int>> ExecuteAsync(CancellationToken cancellationToken = default)
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
                // Still Pending here means the completion event that should have cancelled this
                // reminder never reached the consumer that watches for it — this count is exactly
                // that loss.
                if (reminder.State == ReminderState.Pending)
                {
                    diagnostics.RecordMissedCancellation();
                }

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
